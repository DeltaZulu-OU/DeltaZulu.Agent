using System.Diagnostics;

namespace DeltaZulu.Agent.DoomDisplay;

/// <summary>
/// Maintains two reusable BGR24 frame slots. Producers replace an unrendered pending frame;
/// the single render loop atomically swaps the newest pending slot to the front slot.
/// A packet returned by <see cref="TrySwapForRender"/> must be rendered synchronously and not retained.
/// </summary>
public sealed class LatestFrameDoubleBuffer
{
    private readonly object gate = new();
    private FrameSlot? front;
    private FrameSlot? back;
    private bool pending;
    private long receivedFrames;
    private long renderedFrames;
    private long replacedPendingFrames;
    private long lastReceivedSequence = -1;
    private long lastRenderedSequence = -1;
    private long receivedFramesInWindow;
    private long renderedFramesInWindow;
    private long metricsWindowStarted = Stopwatch.GetTimestamp();
    private double receiveFramesPerSecond;
    private double renderedFramesPerSecond;

    /// <summary>
    /// Copies a validated incoming frame into the writable back slot. If a previous frame was
    /// pending presentation, it is deliberately replaced and counted as discarded.
    /// </summary>
    public void Submit(DoomFramePacket packet)
    {
        DoomFrameCodec.Validate(packet);

        lock (gate)
        {
            if (RequiresReallocation(packet))
            {
                if (pending)
                {
                    replacedPendingFrames++;
                }

                front = new FrameSlot(packet.Width, packet.Height, packet.PixelFormat);
                back = new FrameSlot(packet.Width, packet.Height, packet.PixelFormat);
                pending = false;
            }
            else if (pending)
            {
                replacedPendingFrames++;
            }

            back!.CopyFrom(packet);
            pending = true;
            receivedFrames++;
            receivedFramesInWindow++;
            lastReceivedSequence = packet.Sequence;
            RefreshFrameRates(Stopwatch.GetTimestamp());
        }
    }

    /// <summary>
    /// Atomically makes the most recent completed back slot the front slot. The returned packet
    /// shares the front-slot byte array and is valid until the next successful swap.
    /// </summary>
    public bool TrySwapForRender(out DoomFramePacket? packet)
    {
        lock (gate)
        {
            if (!pending)
            {
                packet = null;
                return false;
            }

            (front, back) = (back, front);
            pending = false;
            packet = front!.ToPacket();
            return true;
        }
    }

    /// <summary>
    /// Records a completed renderer presentation. Call this only after the renderer returns successfully.
    /// </summary>
    public FrameBufferMetrics RecordRendered(long sequence)
    {
        lock (gate)
        {
            renderedFrames++;
            renderedFramesInWindow++;
            lastRenderedSequence = sequence;
            RefreshFrameRates(Stopwatch.GetTimestamp());
            return CreateMetrics();
        }
    }

    public FrameBufferMetrics GetMetrics()
    {
        lock (gate)
        {
            RefreshFrameRates(Stopwatch.GetTimestamp());
            return CreateMetrics();
        }
    }

    private bool RequiresReallocation(DoomFramePacket packet) =>
        front is null ||
        back is null ||
        front.Width != packet.Width ||
        front.Height != packet.Height ||
        front.PixelFormat != packet.PixelFormat;

    private void RefreshFrameRates(long now)
    {
        var elapsed = Stopwatch.GetElapsedTime(metricsWindowStarted, now);
        if (elapsed < TimeSpan.FromSeconds(1))
        {
            return;
        }

        receiveFramesPerSecond = receivedFramesInWindow / elapsed.TotalSeconds;
        renderedFramesPerSecond = renderedFramesInWindow / elapsed.TotalSeconds;
        receivedFramesInWindow = 0;
        renderedFramesInWindow = 0;
        metricsWindowStarted = now;
    }

    private FrameBufferMetrics CreateMetrics() => new(
        receivedFrames,
        renderedFrames,
        replacedPendingFrames,
        receiveFramesPerSecond,
        renderedFramesPerSecond,
        lastReceivedSequence,
        lastRenderedSequence);

    private sealed class FrameSlot(int width, int height, DoomPixelFormat pixelFormat)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public DoomPixelFormat PixelFormat { get; } = pixelFormat;
        public byte[] Pixels { get; } = GC.AllocateUninitializedArray<byte>(
            checked(width * height * DoomFrameCodec.BytesPerPixel));
        public long Sequence { get; private set; }
        public long CapturedAtUnixTimeMilliseconds { get; private set; }

        public void CopyFrom(DoomFramePacket packet)
        {
            packet.Pixels.CopyTo(Pixels, 0);
            Sequence = packet.Sequence;
            CapturedAtUnixTimeMilliseconds = packet.CapturedAtUnixTimeMilliseconds;
        }

        public DoomFramePacket ToPacket() => new() {
            Sequence = Sequence,
            Width = Width,
            Height = Height,
            PixelFormat = PixelFormat,
            Pixels = Pixels,
            CapturedAtUnixTimeMilliseconds = CapturedAtUnixTimeMilliseconds
        };
    }
}

public readonly record struct FrameBufferMetrics(
    long ReceivedFrames,
    long RenderedFrames,
    long ReplacedPendingFrames,
    double ReceiveFramesPerSecond,
    double RenderedFramesPerSecond,
    long LastReceivedSequence,
    long LastRenderedSequence);
