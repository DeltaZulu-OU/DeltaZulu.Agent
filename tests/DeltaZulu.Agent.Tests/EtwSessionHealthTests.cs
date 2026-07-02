using DeltaZulu.Pipeline.Inputs.Etw;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class EtwSessionHealthTests
{
    [TestMethod]
    public void FromMetrics_Healthy_WhenExpectedProviderObservedWithoutLoss()
    {
        var metrics = new EtwCollectorMetrics();
        metrics.IncrementEventsReceived();
        metrics.IncrementEventsEnqueued();

        var snapshot = EtwSessionHealthSnapshot.FromMetrics(
            "DeltaZulu-Kernel-File",
            "windows.etw.kernel-file",
            "1.1.0",
            "Microsoft-Windows-Kernel-File",
            Guid.Parse("90cbdc39-4a3e-11d1-84f4-0000f80464e3"),
            expectedEnabled: true,
            observedEnabled: true,
            observedLevel: "Informational",
            observedKeywords: 0,
            metrics,
            eventsDroppedByProfile: 10,
            DateTimeOffset.Parse("2026-07-01T18:22:31Z"),
            collectionMode: "managed",
            parserName: "WindowsEtw.Native",
            parserVersion: "1.0.0",
            filterVersion: "1.1.0");

        Assert.AreEqual("EtwSessionHealth", snapshot.EventType);
        Assert.AreEqual(EtwSessionHealthStatus.Healthy, snapshot.Status);
        Assert.AreEqual(1, snapshot.EventsReceived);
        Assert.AreEqual(1, snapshot.EventsEmitted);
        Assert.AreEqual(10, snapshot.EventsDroppedByProfile);
        Assert.AreEqual(0, snapshot.EtwEventsLost);
        Assert.AreEqual(0, snapshot.ParserFailures);
    }

    [TestMethod]
    public void FromMetrics_Degraded_WhenLossOrParserFailuresObserved()
    {
        var metrics = new EtwCollectorMetrics();
        metrics.AddEventsLostByEtw(2);
        metrics.IncrementParserFailures();

        var snapshot = EtwSessionHealthSnapshot.FromMetrics(
            "DeltaZulu-Kernel-File",
            "windows.etw.kernel-file",
            "1.1.0",
            "Microsoft-Windows-Kernel-File",
            null,
            expectedEnabled: true,
            observedEnabled: true,
            observedLevel: null,
            observedKeywords: null,
            metrics,
            eventsDroppedByProfile: 0,
            DateTimeOffset.Parse("2026-07-01T18:22:31Z"));

        Assert.AreEqual(EtwSessionHealthStatus.Degraded, snapshot.Status);
        Assert.AreEqual(2, snapshot.EtwEventsLost);
        Assert.AreEqual(1, snapshot.ParserFailures);
    }

    [TestMethod]
    public void FromMetrics_ProviderDisabled_WhenExpectedProviderNotObserved()
    {
        var snapshot = EtwSessionHealthSnapshot.FromMetrics(
            "DeltaZulu-Kernel-File",
            "windows.etw.kernel-file",
            "1.1.0",
            "Microsoft-Windows-Kernel-File",
            null,
            expectedEnabled: true,
            observedEnabled: false,
            observedLevel: null,
            observedKeywords: null,
            new EtwCollectorMetrics(),
            eventsDroppedByProfile: 0,
            DateTimeOffset.Parse("2026-07-01T18:22:31Z"));

        Assert.AreEqual(EtwSessionHealthStatus.ProviderDisabled, snapshot.Status);
    }

    [TestMethod]
    public void ForensicAlignmentMetadata_PreservesNativeIdentityAndProvenance()
    {
        var alignment = new EtwForensicAlignmentMetadata(
            "host-1",
            DateTimeOffset.Parse("2026-07-01T18:22:31Z"),
            123456789,
            Guid.Parse("90cbdc39-4a3e-11d1-84f4-0000f80464e3"),
            "Microsoft-Windows-Kernel-File",
            0,
            67,
            3,
            4820,
            9124,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            null,
            "DeltaZulu-Kernel-File",
            "windows.etw.kernel-file",
            "1.1.0",
            "WindowsEtw.Native",
            "1.0.0",
            "DeltaZulu.Etw.FileActivity/1.0",
            "sha256:abc");

        Assert.AreEqual(67, alignment.Opcode);
        Assert.AreEqual("DeltaZulu-Kernel-File", alignment.EtwSessionName);
        Assert.AreEqual("sha256:abc", alignment.RawPayloadHash);
    }

    [TestMethod]
    public void ResourceDescriptor_AllowsDiagnosticEtwScope()
    {
        var descriptor = new ResourceDescriptor {
            Platform = "windows",
            Family = "etw",
            Mode = "diagnostic",
            Scope = "deltazulu-owned-sessions"
        };

        Assert.AreEqual("diagnostic", descriptor.Mode);
        Assert.AreEqual("deltazulu-owned-sessions", descriptor.Scope);
    }
}
