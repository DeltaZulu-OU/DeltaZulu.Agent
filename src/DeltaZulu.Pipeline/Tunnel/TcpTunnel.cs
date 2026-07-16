using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace DeltaZulu.Pipeline.Tunnel;

/// <summary>
/// Transparent TCP tunnel. The local side accepts plaintext TCP from a pipeline
/// or other local producers, while the remote side is optionally protected with TLS/mTLS.
/// </summary>
public sealed class TcpTunnel : IAsyncDisposable, IDisposable
{
    private readonly List<Task> _connections = [];
    private readonly Lock _connectionsLock = new();
    private readonly TcpTunnelOptions _options;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _acceptLoop;
    private int _disposeStarted;
    private int _endpointIndex;
    private TcpListener? _listener;

    public TcpTunnel(TcpTunnelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);
        _options = options;
    }

    public TunnelEndpoint ListenEndpoint => _options.ListenEndpoint;

    private TunnelEndpoint CurrentEndpoint => _options.Endpoints[Math.Abs(_endpointIndex % _options.Endpoints.Count)];

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _stopping.Cancel();
        _listener?.Stop();
        try
        {
            _acceptLoop?.GetAwaiter().GetResult();
            Task.WaitAll(GetConnectionSnapshot(), TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Shutdown is best-effort; connection tasks observe the cancellation token and socket closure.
        }
        finally
        {
            _stopping.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        _listener?.Stop();
        try
        {
            if (_acceptLoop is not null)
            {
                await _acceptLoop.ConfigureAwait(false);
            }

            await Task.WhenAll(GetConnectionSnapshot()).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch
        {
            // Shutdown is best-effort; connection tasks observe the cancellation token and socket closure.
        }
        finally
        {
            _stopping.Dispose();
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
        if (_listener is not null)
        {
            return;
        }

        _listener = new TcpListener(ParseListenAddress(_options.ListenEndpoint.Host), _options.ListenEndpoint.Port);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_stopping.Token);
    }

    private static IPAddress ParseListenAddress(string host)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return address;
        }

        return Dns.GetHostAddresses(host).FirstOrDefault()
            ?? throw new InvalidDataException($"Tunnel listen host '{host}' could not be resolved.");
    }

    private static async Task PipeAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        try
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        catch when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
    }

    private static void Validate(TcpTunnelOptions options)
    {
        ValidateEndpoint(options.ListenEndpoint, "listen");
        if (options.Endpoints.Count == 0)
        {
            throw new ArgumentException("At least one remote tunnel endpoint is required.", nameof(options));
        }

        for (var index = 0; index < options.Endpoints.Count; index++)
        {
            ValidateEndpoint(options.Endpoints[index], $"remote endpoint {index + 1}");
        }
    }

    private static void ValidateEndpoint(TunnelEndpoint endpoint, string name)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Host))
        {
            throw new ArgumentException($"Tunnel {name} host is required.");
        }

        if (endpoint.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint.Port, $"Tunnel {name} port must be between 1 and 65535.");
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        var listener = _listener ?? throw new InvalidOperationException("The TCP tunnel listener has not been started.");
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient localClient;
            try
            {
                localClient = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposeStarted) != 0)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposeStarted) != 0)
            {
                break;
            }

            TrackConnection(HandleConnectionAsync(localClient, cancellationToken));
        }
    }

    private void AdvanceEndpoint()
    {
        if (_options.Endpoints.Count > 1)
        {
            _endpointIndex = (_endpointIndex + 1) % _options.Endpoints.Count;
        }
    }

    private Task[] GetConnectionSnapshot()
    {
        lock (_connectionsLock)
        {
            return [.. _connections];
        }
    }

    private async Task HandleConnectionAsync(TcpClient localClient, CancellationToken cancellationToken)
    {
        using var localRegistration = localClient;
        using var remoteClient = new TcpClient();
        var endpoint = CurrentEndpoint;

        try
        {
            await remoteClient.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken).ConfigureAwait(false);
            await using var remoteStream = await OpenRemoteStreamAsync(remoteClient, endpoint, cancellationToken).ConfigureAwait(false);
            await using var localStream = localClient.GetStream();

            var localToRemote = PipeAsync(localStream, remoteStream, cancellationToken);
            var remoteToLocal = PipeAsync(remoteStream, localStream, cancellationToken);
            await Task.WhenAny(localToRemote, remoteToLocal).ConfigureAwait(false);
        }
        catch when (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposeStarted) != 0)
        {
            // Expected during shutdown.
        }
        catch
        {
            AdvanceEndpoint();
        }
    }

    private async ValueTask<Stream> OpenRemoteStreamAsync(TcpClient remoteClient, TunnelEndpoint endpoint, CancellationToken cancellationToken)
    {
        var networkStream = remoteClient.GetStream();
        if (!_options.UseTls)
        {
            return networkStream;
        }

        var sslStream = new SslStream(
            networkStream,
            leaveInnerStreamOpen: false,
            _options.ServerCertificateValidationCallback);
        try
        {
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                TargetHost = endpoint.Host,
                ClientCertificates = await _options.GetClientCertificatesAsync(cancellationToken).ConfigureAwait(false),
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, cancellationToken).ConfigureAwait(false);
            return sslStream;
        }
        catch
        {
            await sslStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void TrackConnection(Task connection)
    {
        lock (_connectionsLock)
        {
            _connections.Add(connection);
        }

        _ = connection.ContinueWith(_ => {
            lock (_connectionsLock)
            {
                _connections.Remove(connection);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
