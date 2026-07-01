using DeltaZulu.Pipeline.Core.Etw;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class IrpOperationTrackerTests
{
    [TestMethod]
    public void ObserveEnd_WithMatchingStart_CompletesOperationWithDurationAndStatus()
    {
        var tracker = new IrpOperationTracker();
        var startedAt = DateTimeOffset.Parse("2026-07-01T18:22:31.410Z");
        var endedAt = DateTimeOffset.Parse("2026-07-01T18:22:31.412Z");

        var start = tracker.ObserveStart(0x88776655, 67, "ReadFile", startedAt, 4820, 9124, 0x12345670, 0x2000000123456);
        var completed = tracker.ObserveEnd(0x88776655, endedAt, 0, 4096);

        Assert.HasCount(1, start);
        Assert.AreEqual(OperationCorrelationSource.Unknown, start[0].OperationCorrelationSource);
        Assert.AreEqual(OperationCorrelationSource.IrpStartEnd, completed.OperationCorrelationSource);
        Assert.AreEqual("ReadFile", completed.Operation);
        Assert.AreEqual(67, completed.OperationCode);
        Assert.AreEqual("ReadFile", completed.OperationName);
        Assert.AreEqual("File", completed.OperationFamily);
        Assert.AreEqual(FileIoOpcodeLookup.OperationNameSource, completed.OperationNameSource);
        Assert.AreEqual(startedAt, completed.OperationStartUtc);
        Assert.AreEqual(endedAt, completed.OperationEndUtc);
        Assert.AreEqual(2.0, completed.OperationDurationMs);
        Assert.AreEqual((uint)0, completed.NtStatus);
        Assert.AreEqual((ulong)4096, completed.ExtraInfo);
        Assert.IsFalse(completed.MissingStartEvent);
        Assert.IsFalse(completed.MissingEndEvent);
    }

    [TestMethod]
    public void FlushIncomplete_EmitsMissingEndForStartedOperation()
    {
        var tracker = new IrpOperationTracker();
        var startedAt = DateTimeOffset.Parse("2026-07-01T18:22:31.410Z");
        tracker.ObserveStart(0x88776655, "WriteFile", startedAt, 4820, 9124, 0x12345670, null);

        var incomplete = tracker.FlushIncomplete();

        Assert.HasCount(1, incomplete);
        Assert.AreEqual(OperationCorrelationSource.MissingEnd, incomplete[0].OperationCorrelationSource);
        Assert.AreEqual("WriteFile", incomplete[0].Operation);
        Assert.IsTrue(incomplete[0].MissingEndEvent);
        Assert.AreEqual(0, tracker.ActiveOperationCount);
    }

    [TestMethod]
    public void ObserveEnd_WithoutStart_EmitsMissingStart()
    {
        var tracker = new IrpOperationTracker();
        var endedAt = DateTimeOffset.Parse("2026-07-01T18:22:31.412Z");

        var missing = tracker.ObserveEnd(0x88776655, endedAt, 0xC0000001, 0);

        Assert.AreEqual(OperationCorrelationSource.MissingStart, missing.OperationCorrelationSource);
        Assert.AreEqual((ulong)0x88776655, missing.Irp);
        Assert.AreEqual(endedAt, missing.OperationEndUtc);
        Assert.AreEqual((uint)0xC0000001, missing.NtStatus);
        Assert.IsTrue(missing.MissingStartEvent);
        Assert.IsFalse(missing.MissingEndEvent);
    }

    [TestMethod]
    public void ObserveStart_WithoutIrp_EmitsWithoutIrpPointEvent()
    {
        var tracker = new IrpOperationTracker();

        var point = tracker.ObserveStart(null, "Cleanup", DateTimeOffset.Parse("2026-07-01T18:22:31.410Z"), 4820, 9124, 0x12345670, null);

        Assert.HasCount(1, point);
        Assert.AreEqual(OperationCorrelationSource.WithoutIrp, point[0].OperationCorrelationSource);
        Assert.AreEqual("Cleanup", point[0].Operation);
        Assert.IsNull(point[0].Irp);
    }

    [TestMethod]
    public void ObserveStart_ReusedIrp_EmitsReusedCorrelationAndStartsReplacement()
    {
        var tracker = new IrpOperationTracker();
        var firstAt = DateTimeOffset.Parse("2026-07-01T18:22:31.410Z");
        var secondAt = firstAt.AddMilliseconds(5);
        tracker.ObserveStart(0x88776655, "ReadFile", firstAt, 4820, 9124, 0x12345670, null);

        var reused = tracker.ObserveStart(0x88776655, "WriteFile", secondAt, 4820, 9124, 0x12345670, null);

        Assert.HasCount(2, reused);
        Assert.AreEqual(OperationCorrelationSource.IrpReused, reused[0].OperationCorrelationSource);
        Assert.AreEqual("ReadFile", reused[0].Operation);
        Assert.IsTrue(reused[0].MissingEndEvent);
        Assert.IsTrue(reused[0].IrpReusedBeforeEnd);
        Assert.AreEqual(OperationCorrelationSource.Unknown, reused[1].OperationCorrelationSource);
        Assert.AreEqual("WriteFile", reused[1].Operation);
        Assert.AreEqual(1, tracker.ActiveOperationCount);
    }

    [TestMethod]
    public void ObserveStart_WhenCapacityExceeded_FlushesOldestAsMissingEnd()
    {
        var tracker = new IrpOperationTracker(maximumActiveOperations: 1);
        var firstAt = DateTimeOffset.Parse("2026-07-01T18:22:31.410Z");
        tracker.ObserveStart(1, "ReadFile", firstAt, 1, 2, null, null);

        var overflow = tracker.ObserveStart(2, "WriteFile", firstAt.AddMilliseconds(1), 1, 2, null, null);

        Assert.HasCount(2, overflow);
        Assert.AreEqual(OperationCorrelationSource.Unknown, overflow[0].OperationCorrelationSource);
        Assert.AreEqual(OperationCorrelationSource.MissingEnd, overflow[1].OperationCorrelationSource);
        Assert.AreEqual((ulong)1, overflow[1].Irp);
        Assert.AreEqual(1, tracker.ActiveOperationCount);
    }

    [TestMethod]
    public void FileInfoClassLookup_ReturnsKnownNameAndNullForUnknown()
    {
        Assert.AreEqual("FileRenameInformation", FileInfoClassLookup.GetName(10));
        Assert.AreEqual("FileIdInformation", FileInfoClassLookup.GetName(59));
        Assert.IsNull(FileInfoClassLookup.GetName(9999));
    }

    [TestMethod]
    public void FileIoOpcodeLookup_ReturnsKnownNameAndNullForUnknown()
    {
        Assert.AreEqual("ReadFile", FileIoOpcodeLookup.GetName(67));
        Assert.AreEqual("EndOperation", FileIoOpcodeLookup.GetName(76));
        Assert.IsNull(FileIoOpcodeLookup.GetName(78));
    }
}
