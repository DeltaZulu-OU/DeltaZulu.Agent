using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Delivery;
using DeltaZulu.Pipeline.Forwarder;
using DeltaZulu.Pipeline.Core.MessagePack;

namespace DeltaZulu.Pipeline.Outputs.Forwarder;

public sealed class ForwarderTransport : IDeliveryTransport, IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan SessionCloseTimeout = TimeSpan.FromSeconds(5);

    private readonly ForwarderOptions _options;
    private readonly MessagePackPayloadSerializer _serializer = new();
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private int _disposeStarted;
    private int _endpointIndex;
    private int _transactionId;
    private TcpClient? _client;
    private Stream? _stream;
    private bool _sessionOpen;

    public ForwarderTransport(ForwarderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ValidateEndpoints(options.GetConfiguredEndpoints());

        _options = options;
    }

    private ForwarderEndpoint CurrentEndpoint {
        get {
            var endpoints = _options.GetConfiguredEndpoints();
            return endpoints[Math.Abs(_endpointIndex % endpoints.Count)];
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _sessionLock.Wait();
        try
        {
            DisposeSessionAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        await _sessionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeSessionAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async ValueTask<DeliveryAck> SendAsync(DeliveryBatch batch, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
        ArgumentNullException.ThrowIfNull(batch);

        await _sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);

            try
            {
                var stream = await GetOrOpenSessionAsync(cancellationToken).ConfigureAwait(false);
                var payload = _serializer.Serialize(batch);
                var response = await WriteFrameAndReadResponseAsync(stream, "syslog", payload, cancellationToken).ConfigureAwait(false);

                return new DeliveryAck {
                    BatchId = batch.BatchId,
                    Accepted = IsSuccessResponse(response),
                    Reason = IsSuccessResponse(response) ? null : $"Forwarder send failed: {response}"
                };
            }
            catch (Exception ex) when (IsTransientForwarderFailure(ex))
            {
                AdvanceEndpoint();
                await ResetSessionAsync(CancellationToken.None).ConfigureAwait(false);
                return new DeliveryAck {
                    BatchId = batch.BatchId,
                    Accepted = false,
                    Reason = $"Forwarder send failed: {ex.Message}"
                };
            }
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private static bool IsTransientForwarderFailure(Exception exception) =>
        exception is IOException
            or SocketException
            or InvalidOperationException
            or TimeoutException;

    private static bool IsSuccessResponse(string response) => response.StartsWith("200 ", StringComparison.Ordinal) || string.Equals(response, "200", StringComparison.Ordinal);

    private static void ValidateEndpoints(IReadOnlyList<ForwarderEndpoint> endpoints)
    {
        if (endpoints.Count == 0)
        {
            throw new ArgumentException("At least one Forwarder endpoint is required.");
        }

        for (var index = 0; index < endpoints.Count; index++)
        {
            var endpoint = endpoints[index];
            if (string.IsNullOrWhiteSpace(endpoint.Host))
            {
                throw new ArgumentException($"Forwarder endpoint {index + 1} host is required.");
            }

            if (endpoint.Port is < 1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(endpoints), endpoint.Port, $"Forwarder endpoint {index + 1} port must be between 1 and 65535.");
            }
        }
    }

    private void AdvanceEndpoint()
    {
        var endpoints = _options.GetConfiguredEndpoints();
        if (endpoints.Count > 1)
        {
            _endpointIndex = (_endpointIndex + 1) % endpoints.Count;
        }
    }

    private async ValueTask DisposeSessionAsync(CancellationToken cancellationToken)
    {
        var stream = _stream;
        var client = _client;
        var shouldClose = _sessionOpen;
        _stream = null;
        _client = null;
        _sessionOpen = false;

        if (stream is not null && shouldClose)
        {
            using var closeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            closeTimeout.CancelAfter(SessionCloseTimeout);
            try
            {
                await WriteFrameAndReadResponseAsync(stream, "close", ReadOnlyMemory<byte>.Empty, closeTimeout.Token).ConfigureAwait(false);
            }
            catch
            {
                // Closing is best-effort during teardown or reconnect after a failed send.
            }
        }

        if (stream is not null)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        client?.Dispose();
    }

    private async ValueTask<Stream> GetOrOpenSessionAsync(CancellationToken cancellationToken)
    {
        if (_stream is not null && _sessionOpen)
        {
            return _stream;
        }

        await DisposeSessionAsync(cancellationToken).ConfigureAwait(false);

        var endpoint = CurrentEndpoint;
        _client = new TcpClient();
        await _client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken).ConfigureAwait(false);
        _stream = _client.GetStream();

        if (_options.UseTls)
        {
            var sslStream = new SslStream(_stream, leaveInnerStreamOpen: false, ValidateServerCertificate);
            var sslOptions = new SslClientAuthenticationOptions {
                TargetHost = endpoint.Host,
                ClientCertificates = _options.ClientCertificates,
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.Online
            };
            await sslStream.AuthenticateAsClientAsync(sslOptions, cancellationToken).ConfigureAwait(false);
            _stream = sslStream;
        }

        var response = await WriteFrameAndReadResponseAsync(_stream, "open", ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        if (!IsSuccessResponse(response))
        {
            throw new InvalidOperationException($"Forwarder open failed: {response}");
        }

        _sessionOpen = true;
        return _stream;
    }

    private bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) =>
        _options.CertificateValidation switch {
            CertificateValidationMode.Disabled => true,
            CertificateValidationMode.Thumbprint => certificate is not null
                && _options.AllowedServerCertificateThumbprints.Contains(certificate.GetCertHashString(), StringComparer.OrdinalIgnoreCase),
            _ => sslPolicyErrors == SslPolicyErrors.None
        };

    private async ValueTask<string> WriteFrameAndReadResponseAsync(Stream stream, string command, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var transactionId = Interlocked.Increment(ref _transactionId);
        await ForwarderFrameCodec.WriteFrameAsync(stream, transactionId, command, payload, cancellationToken).ConfigureAwait(false);
        var response = ForwarderFrameCodec.ReadFrameOrThrow(await ForwarderFrameCodec.ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false));
        if (response.Command != "rsp" || response.TransactionId != transactionId)
        {
            throw new InvalidDataException($"Unexpected FORWARDER response frame '{response.Command}' for transaction {response.TransactionId}.");
        }

        return System.Text.Encoding.UTF8.GetString(response.Payload.Span);
    }

    private async ValueTask ResetSessionAsync(CancellationToken cancellationToken) =>
        await DisposeSessionAsync(cancellationToken).ConfigureAwait(false);
}
