namespace DeltaZulu.Agent.DoomDisplay.Outputs;

public interface IDoomOutputSink
{
    FrameBufferMetrics Metrics { get; }

    DoomReliabilityMetrics ReliabilityMetrics { get; }

    void Write(DoomFramePacket packet);

    /// <summary>Accepts an agent health reading for display alongside the video.</summary>
    void WriteHealth(AgentHealthSnapshot snapshot);

    Task RenderUntilCanceledAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A display-oriented sink. It accepts frames quickly, keeps only the latest unrendered frame,
/// and performs the front/back swap on one render loop. It never queues an unbounded stream of frames.
/// </summary>
public sealed class DoomOutputSink : IDoomOutputSink, IDisposable
{
    private readonly LatestFrameDoubleBuffer frameBuffer = new();
    private readonly AgentHealthDisplayState healthState = new();
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

    /// <summary>The newest agent health reading, or <see langword="null" /> before the first one.</summary>
    public AgentHealthView? Health => healthState.View();

    /// <summary>
    /// Copies the completed input frame into the back slot. A newer input replaces an older pending frame.
    /// </summary>
    public void Write(DoomFramePacket packet)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        frameBuffer.Submit(packet);
        telemetry.RecordCollectorAccepted(packet);
    }

    /// <summary>
    /// Replaces the displayed agent health. Health follows the same latest-wins policy as the
    /// pixel stream, so a slow renderer can never build a backlog of stale readings.
    /// </summary>
    public void WriteHealth(AgentHealthSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        healthState.Update(snapshot);
        telemetry.RecordCollectorHealthAccepted();
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
            DoomFramePacket? presented = null;
            var presentedHealthRevision = -1L;
            while (await refreshTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var healthRevision = healthState.Revision;
                if (frameBuffer.TrySwapForRender(out var packet))
                {
                    presented = packet ?? throw new InvalidOperationException(
                        "The double buffer reported a successful swap without a renderable frame.");
                    _ = frameBuffer.RecordRendered(presented.Sequence);
                }
                else if (presented is null || healthRevision == presentedHealthRevision)
                {
                    // Nothing new to show: no frame swapped in and health has not changed.
                    continue;
                }

                // The front slot is only swapped by this loop, so the retained packet stays valid.
                presentedHealthRevision = healthRevision;
                renderer.Render(presented, frameBuffer.GetMetrics(), telemetry.Snapshot(), healthState.View());
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
