using System.Net.Sockets;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Delivery;
using DeltaZulu.Forward;
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
    private ForwardConnection? _connection;
    private ForwardSession? _session;

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
                var session = await GetOrOpenSessionAsync(cancellationToken).ConfigureAwait(false);
                var payload = _serializer.Serialize(batch);
                await session.SendRawEnvelopeAsync(payload, cancellationToken).ConfigureAwait(false);

                return new DeliveryAck {
                    BatchId = batch.BatchId,
                    Accepted = true
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
        var session = _session;
        var connection = _connection;
        _session = null;
        _connection = null;

        if (session is not null)
        {
            using var closeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            closeTimeout.CancelAfter(SessionCloseTimeout);
            try
            {
                if (session.IsActive)
                {
                    await session.CloseAsync(closeTimeout.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                // Closing is best-effort during teardown or reconnect after a failed send.
            }

            await session.DisposeAsync().ConfigureAwait(false);
        }

        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask<ForwardSession> GetOrOpenSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is { IsActive: true } activeSession)
        {
            return activeSession;
        }

        await DisposeSessionAsync(cancellationToken).ConfigureAwait(false);

        if (_options.UseTls)
        {
            throw new InvalidOperationException("DeltaZulu.Forward sessions do not support the legacy forwarder TLS stream wrapper in this Agent integration path.");
        }

        var endpoint = CurrentEndpoint;
        _connection = new ForwardConnection(endpoint.Host, endpoint.Port);
        await _connection.ConnectAsync(cancellationToken).ConfigureAwait(false);

        _session = new ForwardSession(_connection, new ForwardSessionOptions());
        await _session.OpenAsync(cancellationToken).ConfigureAwait(false);
        return _session;
    }

    private async ValueTask ResetSessionAsync(CancellationToken cancellationToken) =>
        await DisposeSessionAsync(cancellationToken).ConfigureAwait(false);
}
