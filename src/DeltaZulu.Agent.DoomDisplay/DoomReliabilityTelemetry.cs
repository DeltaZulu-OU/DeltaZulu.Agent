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
    private long sessionFaults;
    private long healthUpdatesSent;
    private long healthUpdatesFailed;
    private long collectorHealthUpdatesAccepted;
    private long collectorHealthUpdatesRejected;
    private long collectorAcceptedFrames;
    private long collectorRejectedFrames;
    private long sequenceGaps;
    private long outOfOrderFrames;
    private long lastCollectorSequence = -1;
    private long lastCaptureAgeMilliseconds = -1;
    private long maximumCaptureAgeMilliseconds;
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

    /// <summary>
    /// Records a session that ended on a transport failure rather than on a controlled turnover.
    /// The reconnection that follows is counted separately by <see cref="RecordReconnect" />.
    /// </summary>
    public void RecordSessionFault() => Interlocked.Increment(ref sessionFaults);

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

        if (packet.CapturedAtUnixTimeMilliseconds > 0)
        {
            var age = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - packet.CapturedAtUnixTimeMilliseconds);
            Interlocked.Exchange(ref lastCaptureAgeMilliseconds, age);
            UpdateMaximum(ref maximumCaptureAgeMilliseconds, age);
        }

        Interlocked.Increment(ref collectorAcceptedFrames);
    }

    public void RecordCollectorRejected() => Interlocked.Increment(ref collectorRejectedFrames);

    /// <summary>Records an agent-health TypedBatch the collector acknowledged as committed.</summary>
    public void RecordHealthUpdateSent() => Interlocked.Increment(ref healthUpdatesSent);

    /// <summary>Records an agent-health TypedBatch that faulted before a successful acknowledgment.</summary>
    public void RecordHealthUpdateFailed() => Interlocked.Increment(ref healthUpdatesFailed);

    public void RecordCollectorHealthAccepted() => Interlocked.Increment(ref collectorHealthUpdatesAccepted);

    public void RecordCollectorHealthRejected() => Interlocked.Increment(ref collectorHealthUpdatesRejected);

    public DoomReliabilityMetrics Snapshot()
    {
        var acknowledgements = Interlocked.Read(ref acknowledgedFrames);
        var totalAcknowledgmentTicks = Interlocked.Read(ref acknowledgmentLatencyTicks);
        var captureAge = Interlocked.Read(ref lastCaptureAgeMilliseconds);

        return new DoomReliabilityMetrics(
            SendAttempts: Interlocked.Read(ref sendAttempts),
            AcknowledgedFrames: acknowledgements,
            FailedSends: Interlocked.Read(ref failedSends),
            Reconnects: Interlocked.Read(ref reconnects),
            SessionFaults: Interlocked.Read(ref sessionFaults),
            HealthUpdatesSent: Interlocked.Read(ref healthUpdatesSent),
            HealthUpdatesFailed: Interlocked.Read(ref healthUpdatesFailed),
            CollectorHealthUpdatesAccepted: Interlocked.Read(ref collectorHealthUpdatesAccepted),
            CollectorHealthUpdatesRejected: Interlocked.Read(ref collectorHealthUpdatesRejected),
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
            LastCaptureAgeMilliseconds: captureAge < 0 ? null : captureAge,
            MaximumCaptureAgeMilliseconds: Interlocked.Read(ref maximumCaptureAgeMilliseconds));
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
    long SessionFaults,
    long HealthUpdatesSent,
    long HealthUpdatesFailed,
    long CollectorHealthUpdatesAccepted,
    long CollectorHealthUpdatesRejected,
    long CollectorAcceptedFrames,
    long CollectorRejectedFrames,
    long SequenceGaps,
    long OutOfOrderFrames,
    double MeanAcknowledgmentLatencyMilliseconds,
    double MaximumAcknowledgmentLatencyMilliseconds,
    long CurrentInFlight,
    long MaximumInFlight,
    long? LastCaptureAgeMilliseconds,
    long MaximumCaptureAgeMilliseconds);
