using System.Text;

namespace DeltaZulu.Agent.DoomDisplay;

public interface IDoomFrameSource
{
    DoomFramePacket Capture(long sequence);
}

public sealed class SyntheticDoomFrameSource(int width, int height) : IDoomFrameSource
{
    public DoomFramePacket Capture(long sequence)
    {
        var packet = new DoomFramePacket {
            Sequence = sequence,
            Width = width,
            Height = height,
            PixelFormat = DoomPixelFormat.Bgr24,
            Pixels = GC.AllocateUninitializedArray<byte>(checked(width * height * DoomFrameCodec.BytesPerPixel)),
            CapturedAtUnixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var horizon = height * 9 / 20;
        var sway = (int)(Math.Sin(sequence * 0.04d) * width / 10d);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (y < horizon)
                {
                    var shade = 20 + (y * 32 / Math.Max(1, horizon));
                    SetPixel(packet.Pixels, width, x, y, shade / 3, shade, shade);
                    continue;
                }

                var depth = y - horizon + 1;
                var perspectiveX = ((x - width / 2 - sway) * 48 / depth) + (int)(sequence / 3);
                var tile = ((perspectiveX / 8) + (depth / 5)) & 1;
                var brightness = Math.Clamp(92 - depth / 2, 18, 92);
                var red = tile == 0 ? brightness : brightness / 2;
                var green = tile == 0 ? brightness / 3 : brightness / 6;
                SetPixel(packet.Pixels, width, x, y, red, green, 12);
            }
        }

        DrawCrosshair(packet.Pixels, width, height);
        DrawImpSilhouette(packet.Pixels, width, height, sway, sequence);
        return packet;
    }

    private static void DrawCrosshair(byte[] pixels, int width, int height)
    {
        var centerX = width / 2;
        var centerY = height / 2;
        for (var offset = -5; offset <= 5; offset++)
        {
            if (offset is >= -1 and <= 1)
            {
                continue;
            }

            SetPixel(pixels, width, centerX + offset, centerY, 220, 210, 120);
            SetPixel(pixels, width, centerX, centerY + offset, 220, 210, 120);
        }
    }

    private static void DrawImpSilhouette(byte[] pixels, int width, int height, int sway, long sequence)
    {
        var bodyWidth = Math.Max(8, width / 11);
        var bodyHeight = Math.Max(11, height / 4);
        var centerX = width / 2 - sway / 3 + (int)(Math.Sin(sequence * 0.025d) * width / 12d);
        var top = height * 11 / 20 - (int)(Math.Cos(sequence * 0.06d) * 2d);

        for (var y = top; y < top + bodyHeight; y++)
        {
            var narrowing = Math.Abs(y - (top + bodyHeight / 2)) * bodyWidth / Math.Max(1, bodyHeight);
            for (var x = centerX - bodyWidth / 2 + narrowing; x <= centerX + bodyWidth / 2 - narrowing; x++)
            {
                var glow = y < top + bodyHeight / 3 ? 160 : 98;
                SetPixel(pixels, width, x, y, glow, 14, 8);
            }
        }

        SetPixel(pixels, width, centerX - bodyWidth / 5, top + bodyHeight / 4, 255, 224, 72);
        SetPixel(pixels, width, centerX + bodyWidth / 5, top + bodyHeight / 4, 255, 224, 72);
    }

    private static void SetPixel(byte[] pixels, int width, int x, int y, int red, int green, int blue)
    {
        var height = pixels.Length / (width * DoomFrameCodec.BytesPerPixel);
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            return;
        }

        var offset = (y * width + x) * DoomFrameCodec.BytesPerPixel;
        pixels[offset] = (byte)blue;
        pixels[offset + 1] = (byte)green;
        pixels[offset + 2] = (byte)red;
    }
}

public sealed class AnsiDoomRenderer : IDisposable
{
    private readonly StringBuilder buffer = new();
    private bool initialized;
    private int width;
    private int height;

    public void Render(
        DoomFramePacket packet,
        FrameBufferMetrics metrics,
        DoomReliabilityMetrics reliability,
        AgentHealthView? health = null)
    {
        DoomFrameCodec.Validate(packet);
        if (!initialized || width != packet.Width || height != packet.Height)
        {
            Console.Write("\u001b[2J\u001b[H\u001b[?25l");
            initialized = true;
            width = packet.Width;
            height = packet.Height;
        }
        else
        {
            Console.Write("\u001b[H");
        }

        buffer.Clear();
        const int sampleWidth = 2;
        const int sampleHeight = 4;
        for (var y = 0; y < packet.Height; y += sampleHeight)
        {
            for (var x = 0; x < packet.Width; x += sampleWidth)
            {
                var top = Average(packet, x, y, sampleWidth, sampleHeight / 2);
                var bottom = Average(packet, x, y + sampleHeight / 2, sampleWidth, sampleHeight / 2);
                _ = buffer.Append("\u001b[38;2;").Append(top.Red).Append(';').Append(top.Green).Append(';').Append(top.Blue)
                    .Append("m\u001b[48;2;").Append(bottom.Red).Append(';').Append(bottom.Green).Append(';').Append(bottom.Blue)
                    .Append("m▀");
            }

            _ = buffer.Append("\u001b[0m\n");
        }

        _ = buffer.Append("\u001b[0m")
            .Append(" seq=").Append(packet.Sequence)
            .Append(" rx=").Append(metrics.ReceiveFramesPerSecond.ToString("F1"))
            .Append(" fps render=").Append(metrics.RenderedFramesPerSecond.ToString("F1"))
            .Append(" drop=").Append(metrics.ReplacedPendingFrames)
            .Append(" presented=").Append(metrics.RenderedFrames)
            .Append(" ack-avg=").Append(reliability.MeanAcknowledgmentLatencyMilliseconds.ToString("F1")).Append("ms")
            .Append(" ack-max=").Append(reliability.MaximumAcknowledgmentLatencyMilliseconds.ToString("F1")).Append("ms")
            .Append(" in-flight=").Append(reliability.CurrentInFlight).Append('/').Append(reliability.MaximumInFlight)
            .Append(" gaps=").Append(reliability.SequenceGaps)
            .Append(" ooo=").Append(reliability.OutOfOrderFrames)
            .Append(" reject=").Append(reliability.CollectorRejectedFrames)
            .Append(" age=").Append(reliability.LastCaptureAgeMilliseconds?.ToString() ?? "n/a").Append("ms")
            .Append("\u001b[K\n");
        AppendHealthPanel(buffer, packet.Width / 2, health, reliability);
        Console.Write(buffer.ToString());
    }

    /// <summary>
    /// Draws the agent-health panel beneath the video. Health arrives over the same Forward
    /// session as the pixels, on the typed telemetry contract, so the panel reports both the
    /// daemon's own state and how fresh the collector's copy of it is.
    /// </summary>
    private static void AppendHealthPanel(
        StringBuilder target,
        int panelWidth,
        AgentHealthView? health,
        DoomReliabilityMetrics reliability)
    {
        var width = Math.Clamp(panelWidth, 40, 160);
        var received = health is { } view
            ? $"{view.ReceivedAgo.TotalSeconds:F1}s ago"
            : "never";
        var accepted = reliability.CollectorHealthUpdatesAccepted;
        var rejected = reliability.CollectorHealthUpdatesRejected;

        AppendRule(target, width, $" Agent health (TypedBatch) - rx {accepted} rej {rejected} - {received} ");

        if (health is not { } current)
        {
            AppendLine(target, width, "  Waiting for the agent to report over the Forward session.");
            return;
        }

        var snapshot = current.Snapshot;
        if (!snapshot.Available)
        {
            AppendLine(target, width, $"  UNAVAILABLE  {snapshot.UnavailableReason}");
            return;
        }

        var observed = snapshot.ObservedAtUtc is { } observedAt
            ? observedAt.UtcDateTime.ToString("HH:mm:ss")
            : "--:--:--";

        AppendLine(target, width,
            $"  {Value(snapshot.AgentId, "unknown-agent")} @ {Value(snapshot.HostId, "unknown-host")}" +
            $"  observed {observed}Z");
        AppendLine(target, width,
            $"  Buffer   {Value(snapshot.BufferState, "unknown"),-10}" +
            $" disk {Bytes(snapshot.DiskBytesUsed)}/{Bytes(snapshot.DiskBytesLimit)}" +
            $"  mem {Bytes(snapshot.MemoryBytesUsed)}" +
            $"  sealed {Count(snapshot.SealedChunkCount)}" +
            $"  oldest {Milliseconds(snapshot.OldestChunkAgeMilliseconds)}");
        AppendLine(target, width,
            $"  Records  accepted {Count(snapshot.RecordsAcceptedTotal)}" +
            $"  rejected {Count(snapshot.RecordsRejectedTotal)}" +
            $"  dropped {Count(snapshot.RecordsDroppedTotal)}" +
            $"  chunks {Count(snapshot.ChunksCompletedTotal)} dead {Count(snapshot.ChunksDeadLetteredTotal)}");
        AppendLine(target, width,
            $"  Output   {(snapshot.TransportIsRunning == true ? "running   " : "stopped   ")}" +
            $" send {Count(snapshot.TransportSendSuccessesTotal)}/{Count(snapshot.TransportSendAttemptsTotal)}" +
            $"  transient {Count(snapshot.TransportTransientFailuresTotal)}" +
            $"  permanent {Count(snapshot.TransportPermanentFailuresTotal)}");
    }

    private static void AppendRule(StringBuilder target, int width, string caption)
    {
        var trimmed = caption.Length > width ? caption[..width] : caption;
        _ = target.Append("\u001b[0m").Append(trimmed).Append(new string('-', width - trimmed.Length))
            .Append("\u001b[K\n");
    }

    private static void AppendLine(StringBuilder target, int width, string text)
    {
        var trimmed = text.Length > width ? text[..width] : text;
        _ = target.Append("\u001b[0m").Append(trimmed).Append("\u001b[K\n");
    }

    private static string Value(string? text, string fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : text;

    private static string Count(long? value) => value?.ToString("N0") ?? "-";

    private static string Milliseconds(double? value) => value is null ? "-" : $"{value.Value:F0}ms";

    private static string Bytes(long? value)
    {
        if (value is null)
        {
            return "-";
        }

        double size = value.Value;
        var units = new[] { "B", "K", "M", "G", "T" };
        var unit = 0;
        while (size >= 1024d && unit < units.Length - 1)
        {
            size /= 1024d;
            unit++;
        }

        return unit == 0 ? $"{value.Value}B" : $"{size:0.#}{units[unit]}";
    }

    public void Dispose()
    {
        if (initialized)
        {
            Console.Write("\u001b[0m\u001b[?25h\n");
        }
    }

    private static Rgb Average(DoomFramePacket packet, int startX, int startY, int sampleWidth, int sampleHeight)
    {
        var endX = Math.Min(packet.Width, startX + sampleWidth);
        var endY = Math.Min(packet.Height, startY + sampleHeight);
        var red = 0;
        var green = 0;
        var blue = 0;
        var count = 0;
        for (var y = startY; y < endY; y++)
        {
            for (var x = startX; x < endX; x++)
            {
                var offset = (y * packet.Width + x) * DoomFrameCodec.BytesPerPixel;
                blue += packet.Pixels[offset];
                green += packet.Pixels[offset + 1];
                red += packet.Pixels[offset + 2];
                count++;
            }
        }

        return new Rgb((byte)(red / count), (byte)(green / count), (byte)(blue / count));
    }

    private readonly record struct Rgb(byte Red, byte Green, byte Blue);
}
