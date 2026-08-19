using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DeltaZulu.Agent.DoomDisplay.Tests;

[TestClass]
public sealed class DoomReliabilityTelemetryTests
{
    [TestMethod]
    public void SendTelemetry_TracksAcknowledgementsFailuresAndBoundedInFlightCount()
    {
        var telemetry = new DoomReliabilityTelemetry();

        telemetry.RecordSendStarted();
        telemetry.RecordSendStarted();
        telemetry.RecordSendAcknowledged(TimeSpan.FromMilliseconds(12));
        telemetry.RecordSendFailed();

        var metrics = telemetry.Snapshot();

        Assert.AreEqual(2L, metrics.SendAttempts);
        Assert.AreEqual(1L, metrics.AcknowledgedFrames);
        Assert.AreEqual(1L, metrics.FailedSends);
        Assert.AreEqual(0L, metrics.CurrentInFlight);
        Assert.AreEqual(2L, metrics.MaximumInFlight);
        Assert.AreEqual(12d, metrics.MeanAcknowledgmentLatencyMilliseconds, 0.001d);
        Assert.AreEqual(12d, metrics.MaximumAcknowledgmentLatencyMilliseconds, 0.001d);
    }

    [TestMethod]
    public void CollectorTelemetry_RecordsSequenceGapsAndOutOfOrderFrames()
    {
        var telemetry = new DoomReliabilityTelemetry();
        var capturedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        telemetry.RecordCollectorAccepted(CreatePacket(4, capturedAt));
        telemetry.RecordCollectorAccepted(CreatePacket(7, capturedAt));
        telemetry.RecordCollectorAccepted(CreatePacket(6, capturedAt));
        telemetry.RecordCollectorRejected();

        var metrics = telemetry.Snapshot();

        Assert.AreEqual(3L, metrics.CollectorAcceptedFrames);
        Assert.AreEqual(1L, metrics.CollectorRejectedFrames);
        Assert.AreEqual(2L, metrics.SequenceGaps);
        Assert.AreEqual(1L, metrics.OutOfOrderFrames);
        Assert.IsNotNull(metrics.LastCaptureAgeMilliseconds);
        Assert.IsTrue(metrics.LastCaptureAgeMilliseconds >= 0);
    }

    [TestMethod]
    public void SessionFaults_AreCountedSeparatelyFromTheReconnectThatFollows()
    {
        var telemetry = new DoomReliabilityTelemetry();

        telemetry.RecordSessionFault();
        telemetry.RecordReconnect();

        var metrics = telemetry.Snapshot();

        Assert.AreEqual(1L, metrics.SessionFaults);
        Assert.AreEqual(1L, metrics.Reconnects);
    }

    [TestMethod]
    public void CaptureAge_IsMeasuredAtAcceptanceAndDoesNotGrowWhileIdle()
    {
        var telemetry = new DoomReliabilityTelemetry();
        telemetry.RecordCollectorAccepted(
            CreatePacket(1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 250));

        var first = telemetry.Snapshot().LastCaptureAgeMilliseconds;
        Thread.Sleep(120);
        var second = telemetry.Snapshot().LastCaptureAgeMilliseconds;

        Assert.IsNotNull(first);
        Assert.IsTrue(first >= 250, $"Expected at least the injected 250 ms of age, observed {first}.");
        Assert.AreEqual(first, second, "The accepted frame's age must not grow while the stream is idle.");
        Assert.AreEqual(first, telemetry.Snapshot().MaximumCaptureAgeMilliseconds);
    }

    [TestMethod]
    public void CaptureAge_IsAbsentWhenTheSourceStampsNoTimestamp()
    {
        var telemetry = new DoomReliabilityTelemetry();
        telemetry.RecordCollectorAccepted(CreatePacket(1, capturedAt: 0));

        var metrics = telemetry.Snapshot();

        Assert.IsNull(metrics.LastCaptureAgeMilliseconds);
        Assert.AreEqual(1L, metrics.CollectorAcceptedFrames);
    }

    [TestMethod]
    public void DoubleBuffer_PreservesCaptureTimestampAcrossSlotSwap()
    {
        var buffer = new LatestFrameDoubleBuffer();
        var capturedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 200;
        buffer.Submit(CreatePacket(9, capturedAt));

        Assert.IsTrue(buffer.TrySwapForRender(out var rendered));
        Assert.IsNotNull(rendered);
        Assert.AreEqual(capturedAt, rendered.CapturedAtUnixTimeMilliseconds);
    }

    private static DoomFramePacket CreatePacket(long sequence, long capturedAt) => new() {
        Sequence = sequence,
        Width = 1,
        Height = 1,
        PixelFormat = DoomPixelFormat.Bgr24,
        Pixels = [11, 22, 33],
        CapturedAtUnixTimeMilliseconds = capturedAt
    };
}
