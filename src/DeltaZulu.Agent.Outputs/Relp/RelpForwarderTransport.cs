using System.Net.Sockets;
using DeltaZulu.Agent.Pipeline.Abstractions;
using DeltaZulu.Agent.Pipeline.Delivery;
using DeltaZulu.Agent.Pipeline.MessagePack;
using DeltaZulu.Relp;

namespace DeltaZulu.Agent.Outputs.Relp;

public sealed class RelpForwarderTransport : IDeliveryTransport, IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan SessionCloseTimeout = TimeSpan.FromSeconds(5);

    private readonly RelpForwarderOptions _options;
    private readonly MessagePackPayloadSerializer _serializer = new();
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private RelpConnection? _connection;
    private RelpSession? _session;
    private int _endpointIndex;
    private int _disposeStarted;

    public RelpForwarderTransport(RelpForwarderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ValidateEndpoints(options.GetConfiguredEndpoints());

        _options = options;
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
                await session.SendMessageAsync(payload, cancellationToken).ConfigureAwait(false);

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
                    Reason = $"RELP send failed: {ex.Message}"
                };
            }
        }
        finally
        {
            _sessionLock.Release();
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

    private async ValueTask<RelpSession> GetOrOpenSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is { IsActive: true })
        {
            return _session;
        }

        await DisposeSessionAsync(cancellationToken).ConfigureAwait(false);

        var endpoint = CurrentEndpoint;
        _connection = new RelpConnection(
            endpoint.Host,
            endpoint.Port,
            _options.UseTls,
            _options.ClientCertificates);
        await _connection.ConnectAsync(cancellationToken).ConfigureAwait(false);

        _session = new RelpSession(_connection);
        await _session.OpenAsync(cancellationToken).ConfigureAwait(false);
        return _session;
    }

    private RelpEndpoint CurrentEndpoint
    {
        get
        {
            var endpoints = _options.GetConfiguredEndpoints();
            return endpoints[Math.Abs(_endpointIndex % endpoints.Count)];
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

    private async ValueTask ResetSessionAsync(CancellationToken cancellationToken) =>
        await DisposeSessionAsync(cancellationToken).ConfigureAwait(false);

    private async ValueTask DisposeSessionAsync(CancellationToken cancellationToken)
    {
        var session = _session;
        var connection = _connection;
        _session = null;
        _connection = null;

        if (session is { IsActive: true })
        {
            using var closeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            closeTimeout.CancelAfter(SessionCloseTimeout);
            try
            {
                await session.CloseAsync(closeTimeout.Token).ConfigureAwait(false);
            }
            catch
            {
                // Closing is best-effort during teardown or reconnect after a failed send.
            }
        }

        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void ValidateEndpoints(IReadOnlyList<RelpEndpoint> endpoints)
    {
        if (endpoints.Count == 0)
        {
            throw new ArgumentException("At least one RELP endpoint is required.");
        }

        for (var index = 0; index < endpoints.Count; index++)
        {
            var endpoint = endpoints[index];
            if (string.IsNullOrWhiteSpace(endpoint.Host))
            {
                throw new ArgumentException($"RELP endpoint {index + 1} host is required.");
            }

            if (endpoint.Port is < 1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(endpoints), endpoint.Port, $"RELP endpoint {index + 1} port must be between 1 and 65535.");
            }
        }
    }

    private static bool IsTransientForwarderFailure(Exception exception) =>
        exception is IOException
            or SocketException
            or InvalidOperationException
            or TimeoutException;
}
