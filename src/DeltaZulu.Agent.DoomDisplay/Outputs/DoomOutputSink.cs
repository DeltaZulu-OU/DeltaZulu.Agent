namespace DeltaZulu.Agent.DoomDisplay.Outputs;

public interface IDoomOutputSink
{
    FrameBufferMetrics Metrics { get; }

    DoomReliabilityMetrics ReliabilityMetrics { get; }

    void Write(DoomFramePacket packet);

    Task RenderUntilCanceledAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A display-oriented sink. It accepts frames quickly, keeps only the latest unrendered frame,
/// and performs the front/back swap on one render loop. It never queues an unbounded stream of frames.
/// </summary>
public sealed class DoomOutputSink : IDoomOutputSink, IDisposable
{
    private readonly LatestFrameDoubleBuffer frameBuffer = new();
    private readonly AnsiDoomRenderer renderer;
    private readonly DoomReliabilityTelemetry telemetry;
    private readonly TimeSpan refreshInterval;
    private int disposed;
    private int renderLoopActive;

    public DoomOutputSink(
        AnsiDoomRenderer renderer,
        DoomReliabilityTelemetry? telemetry = null,
        int maximumFramesPerSecond = 30)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (maximumFramesPerSecond is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFramesPerSecond),
                maximumFramesPerSecond,
                "The renderer refresh rate must be between 1 and 120 FPS.");
        }

        this.renderer = renderer;
        this.telemetry = telemetry ?? new DoomReliabilityTelemetry();
        refreshInterval = TimeSpan.FromSeconds(1d / maximumFramesPerSecond);
    }

    public FrameBufferMetrics Metrics => frameBuffer.GetMetrics();

    public DoomReliabilityMetrics ReliabilityMetrics => telemetry.Snapshot();

    /// <summary>
    /// Copies the completed input frame into the back slot. A newer input replaces an older pending frame.
    /// </summary>
    public void Write(DoomFramePacket packet)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        frameBuffer.Submit(packet);
        telemetry.RecordCollectorAccepted(packet);
    }

    public async Task RenderUntilCanceledAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.CompareExchange(ref renderLoopActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("Only one render loop may consume the Doom output sink.");
        }

        try
        {
            using var refreshTimer = new PeriodicTimer(refreshInterval);
            while (await refreshTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!frameBuffer.TrySwapForRender(out var packet))
                {
                    continue;
                }

                var frame = packet ?? throw new InvalidOperationException(
                    "The double buffer reported a successful swap without a renderable frame.");
                _ = frameBuffer.RecordRendered(frame.Sequence);
                renderer.Render(frame, frameBuffer.GetMetrics(), telemetry.Snapshot());
            }
        }
        finally
        {
            Volatile.Write(ref renderLoopActive, 0);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            renderer.Dispose();
        }
    }
}
