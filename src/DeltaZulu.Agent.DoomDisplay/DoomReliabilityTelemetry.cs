namespace DeltaZulu.Agent.DoomDisplay;

/// <summary>
/// Thread-safe operational telemetry for the Forward reliability demonstration. It records transport
/// outcomes and collector continuity without retaining frame payloads or modifying Forward semantics.
/// </summary>
public sealed class DoomReliabilityTelemetry
{
    private long sendAttempts;
    private long acknowledgedFrames;
    private long failedSends;
    private long reconnects;
    private long collectorAcceptedFrames;
    private long collectorRejectedFrames;
    private long sequenceGaps;
    private long outOfOrderFrames;
    private long lastCollectorSequence = -1;
    private long lastCapturedAtUnixTimeMilliseconds;
    private long acknowledgmentLatencyTicks;
    private long maximumAcknowledgmentLatencyTicks;
    private long maximumInFlight;
    private long currentInFlight;

    public void RecordSendStarted()
    {
        Interlocked.Increment(ref sendAttempts);
        var inFlight = Interlocked.Increment(ref currentInFlight);
        UpdateMaximum(ref maximumInFlight, inFlight);
    }

    public void RecordSendAcknowledged(TimeSpan elapsed)
    {
        Interlocked.Increment(ref acknowledgedFrames);
        Interlocked.Add(ref acknowledgmentLatencyTicks, elapsed.Ticks);
        UpdateMaximum(ref maximumAcknowledgmentLatencyTicks, elapsed.Ticks);
        Interlocked.Decrement(ref currentInFlight);
    }

    public void RecordSendFailed()
    {
        Interlocked.Increment(ref failedSends);
        Interlocked.Decrement(ref currentInFlight);
    }

    public void RecordReconnect() => Interlocked.Increment(ref reconnects);

    public void RecordCollectorAccepted(DoomFramePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var previous = Interlocked.Exchange(ref lastCollectorSequence, packet.Sequence);
        if (previous >= 0)
        {
            if (packet.Sequence > previous + 1)
            {
                Interlocked.Add(ref sequenceGaps, packet.Sequence - previous - 1);
            }
            else if (packet.Sequence <= previous)
            {
                Interlocked.Increment(ref outOfOrderFrames);
            }
        }

        Interlocked.Exchange(ref lastCapturedAtUnixTimeMilliseconds, packet.CapturedAtUnixTimeMilliseconds);
        Interlocked.Increment(ref collectorAcceptedFrames);
    }

    public void RecordCollectorRejected() => Interlocked.Increment(ref collectorRejectedFrames);

    public DoomReliabilityMetrics Snapshot()
    {
        var acknowledgements = Interlocked.Read(ref acknowledgedFrames);
        var totalAcknowledgmentTicks = Interlocked.Read(ref acknowledgmentLatencyTicks);
        var captureTimestamp = Interlocked.Read(ref lastCapturedAtUnixTimeMilliseconds);
        var captureAge = captureTimestamp <= 0
            ? (long?)null
            : Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - captureTimestamp);

        return new DoomReliabilityMetrics(
            SendAttempts: Interlocked.Read(ref sendAttempts),
            AcknowledgedFrames: acknowledgements,
            FailedSends: Interlocked.Read(ref failedSends),
            Reconnects: Interlocked.Read(ref reconnects),
            CollectorAcceptedFrames: Interlocked.Read(ref collectorAcceptedFrames),
            CollectorRejectedFrames: Interlocked.Read(ref collectorRejectedFrames),
            SequenceGaps: Interlocked.Read(ref sequenceGaps),
            OutOfOrderFrames: Interlocked.Read(ref outOfOrderFrames),
            MeanAcknowledgmentLatencyMilliseconds: acknowledgements == 0
                ? 0
                : TimeSpan.FromTicks(totalAcknowledgmentTicks / acknowledgements).TotalMilliseconds,
            MaximumAcknowledgmentLatencyMilliseconds: TimeSpan.FromTicks(
                Interlocked.Read(ref maximumAcknowledgmentLatencyTicks)).TotalMilliseconds,
            CurrentInFlight: Math.Max(0, Interlocked.Read(ref currentInFlight)),
            MaximumInFlight: Interlocked.Read(ref maximumInFlight),
            LastCaptureAgeMilliseconds: captureAge);
    }

    private static void UpdateMaximum(ref long location, long value)
    {
        long observed;
        do
        {
            observed = Interlocked.Read(ref location);
            if (value <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref location, value, observed) != observed);
    }
}

public readonly record struct DoomReliabilityMetrics(
    long SendAttempts,
    long AcknowledgedFrames,
    long FailedSends,
    long Reconnects,
    long CollectorAcceptedFrames,
    long CollectorRejectedFrames,
    long SequenceGaps,
    long OutOfOrderFrames,
    double MeanAcknowledgmentLatencyMilliseconds,
    double MaximumAcknowledgmentLatencyMilliseconds,
    long CurrentInFlight,
    long MaximumInFlight,
    long? LastCaptureAgeMilliseconds);
