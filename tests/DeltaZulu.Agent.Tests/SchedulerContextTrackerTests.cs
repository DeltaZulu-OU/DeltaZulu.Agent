using DeltaZulu.Pipeline.Inputs.Etw;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class SchedulerContextTrackerTests
{
    [TestMethod]
    public void ObserveCSwitch_WithWaitingOldThread_StoresWaitingState()
    {
        var tracker = new SchedulerContextTracker();
        var timestamp = DateTimeOffset.Parse("2026-07-01T18:22:31.410Z");

        var state = tracker.ObserveCSwitch(0, 9124, 0x05, 0x0B, 1000, 8, timestamp);

        Assert.AreEqual(9124, state.ThreadId);
        Assert.AreEqual(ThreadIdentityStatus.Resolved, state.ThreadIdentityStatus);
        Assert.AreEqual("Waiting", state.ThreadState);
        Assert.AreEqual(0x05, state.ThreadStateCode);
        Assert.AreEqual("WrDelayExecution", state.ThreadWaitReason);
        Assert.AreEqual(ThreadWaitCategory.IoWait, state.ThreadWaitCategory);
    }

    [TestMethod]
    public void ObserveCSwitch_WithPageInWait_ClassifiesIoWait()
    {
        var tracker = new SchedulerContextTracker();

        var state = tracker.ObserveCSwitch(0, 9124, 0x05, 0x02, 1000, 8, DateTimeOffset.Parse("2026-07-01T18:22:31.410Z"));

        Assert.AreEqual("PageIn", state.ThreadWaitReason);
        Assert.AreEqual(ThreadWaitCategory.IoWait, state.ThreadWaitCategory);
        Assert.IsTrue(state.ThreadWasInIoWait);
    }

    [TestMethod]
    public void ObserveCSwitch_WithWrPageInWait_ClassifiesIoWait()
    {
        var tracker = new SchedulerContextTracker();

        var state = tracker.ObserveCSwitch(0, 9124, 0x05, 0x09, 1000, 8, DateTimeOffset.Parse("2026-07-01T18:22:31.410Z"));

        Assert.AreEqual("WrPageIn", state.ThreadWaitReason);
        Assert.AreEqual(ThreadWaitCategory.IoWait, state.ThreadWaitCategory);
        Assert.IsTrue(state.ThreadWasInIoWait);
    }

    [TestMethod]
    public void ObserveCSwitch_WithNonIoWait_ClassifiesNonIoWait()
    {
        var tracker = new SchedulerContextTracker();

        var state = tracker.ObserveCSwitch(0, 9124, 0x05, 0x1D, 1000, 8, DateTimeOffset.Parse("2026-07-01T18:22:31.410Z"));

        Assert.AreEqual("WrMutex", state.ThreadWaitReason);
        Assert.AreEqual(ThreadWaitCategory.Synchronization, state.ThreadWaitCategory);
        Assert.IsFalse(state.ThreadWasInIoWait);
    }

    [TestMethod]
    public void ObserveReadyThread_RecordsWakeeAndWakerRelationship()
    {
        var tracker = new SchedulerContextTracker();
        var timestamp = DateTimeOffset.Parse("2026-07-01T18:22:31.410Z");

        var relationship = tracker.ObserveReadyThread(timestamp, 4000, 9124);
        var wakeeState = tracker.ResolveThreadState(9124, timestamp.AddMilliseconds(4));

        Assert.AreEqual(4000, relationship.WakerThread.ThreadId);
        Assert.AreEqual(9124, relationship.WakeeThread.ThreadId);
        Assert.IsNotNull(wakeeState);
        Assert.AreEqual("Ready", wakeeState.ThreadState);
        Assert.AreEqual("ReadyThread", wakeeState.ThreadStateSource);
        Assert.AreEqual(4000, wakeeState.WakerThreadId);
        Assert.AreEqual(9124, wakeeState.WakeeThreadId);
        Assert.AreEqual(4, wakeeState.AgeMs(timestamp.AddMilliseconds(4)));
    }

    [TestMethod]
    public void ThreadIdentity_FromMissingThreadId_MarksMissing()
    {
        var identity = ThreadIdentity.FromEtwThreadId(null);

        Assert.IsNull(identity.ThreadId);
        Assert.AreEqual(ThreadIdentityStatus.Missing, identity.Status);
    }

    [TestMethod]
    public void ThreadIdentity_FromAnonymizedThreadId_MarksAnonymized()
    {
        var identity = ThreadIdentity.FromEtwThreadId(ThreadIdentity.AnonymizedThreadId);

        Assert.IsNull(identity.ThreadId);
        Assert.AreEqual(ThreadIdentityStatus.Anonymized, identity.Status);
    }

    [TestMethod]
    public void ObserveCSwitch_WithInvalidThreadState_PreservesRawStateAndFlagsDiagnostic()
    {
        var tracker = new SchedulerContextTracker();

        var state = tracker.ObserveCSwitch(0, 9124, 0xFF, 0x02, 1000, 8, DateTimeOffset.Parse("2026-07-01T18:22:31.410Z"));

        Assert.AreEqual(0xFF, state.ThreadStateCode);
        Assert.IsNull(state.ThreadState);
        Assert.IsTrue(tracker.LastSwitchHadInvalidPreviousState);
    }

    [TestMethod]
    public void ObserveCSwitch_WithMismatchedPreviousThread_FlagsDiagnostic()
    {
        var tracker = new SchedulerContextTracker();
        var timestamp = DateTimeOffset.Parse("2026-07-01T18:22:31.410Z");
        tracker.ObserveCSwitch(0, 1, 0x05, 0x02, 1000, 8, timestamp);

        tracker.ObserveCSwitch(0, 2000, 0x05, 0x02, 3000, 8, timestamp.AddMilliseconds(1));

        Assert.IsTrue(tracker.LastSwitchHadMismatchedPreviousThread);
    }

    [TestMethod]
    public void WaitReasonLookup_UnknownWaitReason_ReturnsUnknownAndNullName()
    {
        var wait = WaitReasonLookup.Classify(0xFF);

        Assert.AreEqual(0xFF, wait.ThreadWaitReasonCode);
        Assert.IsNull(wait.ThreadWaitReason);
        Assert.AreEqual(ThreadWaitCategory.Unknown, wait.ThreadWaitCategory);
        Assert.IsFalse(wait.ThreadWasInIoWait);
    }
}
