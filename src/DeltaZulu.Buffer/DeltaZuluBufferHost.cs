using System.Threading.Channels;
using DeltaZulu.Buffer.Abstractions;
using DeltaZulu.Buffer.Chunks;
using DeltaZulu.Buffer.Configuration;
using DeltaZulu.Buffer.Dispatch;
using DeltaZulu.Buffer.Metrics;
using DeltaZulu.Buffer.Recovery;
using DeltaZulu.Buffer.Retry;
using DeltaZulu.Buffer.Storage;
using Microsoft.Extensions.Logging;

namespace DeltaZulu.Buffer;

public sealed class DeltaZuluBufferHost<T> : IAsyncDisposable
{
    private readonly DeltaZuluBuffer<T> _buffer;
    private readonly DispatchWorker _dispatchWorker;
    private readonly FileSystemRecoveryManager _recoveryManager;
    private readonly BufferEventBroadcaster _eventBroadcaster;
    private readonly BufferMetricsCounter _metrics;
    private readonly FileChunkStore _store;
    private readonly Channel<StoredChunk> _dispatchChannel;
    private readonly DeltaZuluBufferOptions _options;
    private readonly ILogger? _logger;

    private Task? _dispatchTask;
    private Task? _rotationTask;
    private CancellationTokenSource? _cts;
    private bool _started;
    private bool _disposed;

    public DeltaZuluBufferHost(
        DeltaZuluBufferOptions options,
        IRecordSerializer<T> serializer,
        IChunkSender sender,
        IRetryScheduler? retryScheduler = null,
        ILoggerFactory? loggerFactory = null)
    {
        _options = options;
        _logger = loggerFactory?.CreateLogger<DeltaZuluBufferHost<T>>();

        _eventBroadcaster = new BufferEventBroadcaster();
        _metrics = new BufferMetricsCounter();
        _metrics.UpdateDiskUsage(0, options.MaxDiskBytes);

        _dispatchChannel = Channel.CreateBounded<StoredChunk>(
            new BoundedChannelOptions(1024) {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

        _store = new FileChunkStore(
            options.StoragePath,
            loggerFactory?.CreateLogger<FileChunkStore>());
        var store = _store;

        var scheduler = retryScheduler ?? new ExponentialBackoffRetryScheduler(options);
        var backpressure = new BackpressureController(options);

        _buffer = new DeltaZuluBuffer<T>(
            serializer, store, options, _metrics,
            _eventBroadcaster, backpressure, _dispatchChannel.Writer);

        _dispatchWorker = new DispatchWorker(
            _dispatchChannel.Reader, sender, store, scheduler,
            options, _metrics, _eventBroadcaster,
            loggerFactory?.CreateLogger<DispatchWorker>());

        _recoveryManager = new FileSystemRecoveryManager(
            store, _dispatchChannel.Writer, _metrics,
            _eventBroadcaster, options.StoragePath,
            loggerFactory?.CreateLogger<FileSystemRecoveryManager>());
    }

    public IDeltaZuluBuffer<T> Buffer => _buffer;
    public IObservable<BufferEvent> Events => _eventBroadcaster;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("Buffer host is already started.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _eventBroadcaster.Publish(BufferEvent.Create(BufferEventType.BufferStarted));

        await _recoveryManager.RecoverAsync(_cts.Token);

        var diskUsed = await _store.GetDiskBytesUsedAsync(_cts.Token);
        _metrics.UpdateDiskUsage(diskUsed, _options.MaxDiskBytes);

        _dispatchTask = Task.Run(() => _dispatchWorker.RunAsync(_cts.Token), _cts.Token);
        _rotationTask = Task.Run(() => RunRotationTimerAsync(_cts.Token), _cts.Token);

        _started = true;
        _logger?.LogInformation("Buffer host started. Storage: {Path}", _options.StoragePath);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_started || _disposed)
        {
            return;
        }

        _buffer.MarkStopping();
        _logger?.LogInformation("Buffer host stopping...");

        await _buffer.FlushAsync(cancellationToken);

        _dispatchChannel.Writer.TryComplete();

        if (_dispatchTask is not null)
        {
            try { await _dispatchTask.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken); }
            catch (TimeoutException) { _logger?.LogWarning("Dispatch worker did not drain within timeout."); }
            catch (OperationCanceledException) { }
        }

        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        if (_rotationTask is not null)
        {
            try { await _rotationTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { }
        }

        _eventBroadcaster.Publish(BufferEvent.Create(BufferEventType.BufferStopped));
        _eventBroadcaster.Complete();

        _started = false;
        _logger?.LogInformation("Buffer host stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_started)
        {
            await StopAsync();
        }

        _buffer.Dispose();
        _cts?.Dispose();
        _disposed = true;
    }

    private async Task RunRotationTimerAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await _buffer.RotateIfStaleAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in rotation timer");
            }
        }
    }
}