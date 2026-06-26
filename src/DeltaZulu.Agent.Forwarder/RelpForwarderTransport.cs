using System.Net.Sockets;
using System.Text.Json;
using Relp;

namespace DeltaZulu.Agent.Forwarder;

public sealed class RelpForwarderTransport : IForwarderTransport, IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan SessionCloseTimeout = TimeSpan.FromSeconds(5);

    private readonly RelpForwarderOptions _options;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private RelpConnection? _connection;
    private RelpSession? _session;
    private int _disposeStarted;

    public RelpForwarderTransport(RelpForwarderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new ArgumentException("RELP host is required.", nameof(options));
        }

        if (options.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Port, "RELP port must be between 1 and 65535.");
        }

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
                var payload = JsonSerializer.SerializeToUtf8Bytes(batch, ForwarderJson.Options);
                await session.SendMessageAsync(payload, cancellationToken).ConfigureAwait(false);

                return new DeliveryAck {
                    BatchId = batch.BatchId,
                    Accepted = true
                };
            }
            catch (Exception ex) when (IsTransientForwarderFailure(ex))
            {
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

        _connection = new RelpConnection(
            _options.Host,
            _options.Port,
            _options.UseTls,
            _options.ClientCertificates);
        await _connection.ConnectAsync(cancellationToken).ConfigureAwait(false);

        _session = new RelpSession(_connection);
        await _session.OpenAsync(cancellationToken).ConfigureAwait(false);
        return _session;
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

    private static bool IsTransientForwarderFailure(Exception exception) =>
        exception is IOException
            or SocketException
            or InvalidOperationException
            or TimeoutException
            or JsonException;
}
