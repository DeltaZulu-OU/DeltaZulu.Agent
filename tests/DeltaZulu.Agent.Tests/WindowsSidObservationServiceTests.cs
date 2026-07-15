using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Enrichment.Windows;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class WindowsSidObservationServiceTests
{
    [TestMethod]
    public void Observe_EmitsSidTableObservationWithoutMutatingProjectedEventFields()
    {
        var cachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var service = new WindowsSidObservationService(cachePath, new StubResolver());
        var fields = CreateSecurityFields(4720);
        fields["TargetUserSid"] = StubResolver.Sid;
        fields["TargetUserName"] = "-";

        var observations = service.Observe(CreateSource(fields), fields);

        Assert.HasCount(1, observations);
        Assert.AreEqual("Sid", observations[0].EventType);
        Assert.AreEqual(StubResolver.Sid, fields["TargetUserSid"]);
        Assert.AreEqual("-", fields["TargetUserName"]);
        Assert.IsFalse(fields.ContainsKey("TargetUserName_resolved"));
        Assert.AreEqual("lenovo_123456", observations[0].ResolvedAccountName);
        Assert.AreEqual("HOST01", observations[0].ResolvedDomainName);
        Assert.AreEqual("HOST01\\lenovo_123456", observations[0].ResolvedCanonicalName);
        Assert.AreEqual("LookupAccountSidW", observations[0].ResolutionSource);
        Assert.AreEqual("Resolved", observations[0].ResolutionStatus);
    }

    [TestMethod]
    public void Observe_UsesDurableSidTableCacheAfterRestartForDeletedLocalAccount()
    {
        var cachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var first = new WindowsSidObservationService(cachePath, new StubResolver());
        var createFields = CreateSecurityFields(4720);
        createFields["TargetUserSid"] = StubResolver.Sid;
        first.Observe(CreateSource(createFields), createFields);

        var restarted = new WindowsSidObservationService(cachePath, new FailingResolver());
        var deleteFields = CreateSecurityFields(4726);
        deleteFields["TargetUserSid"] = StubResolver.Sid;
        var observations = restarted.Observe(CreateSource(deleteFields), deleteFields);

        Assert.AreEqual("lenovo_123456", observations.Single().ResolvedAccountName);
        Assert.AreEqual("Sid", observations.Single().ResolutionSource);
        Assert.AreEqual("DeletedObserved", observations.Single().LifecycleStatus);
        Assert.IsNotNull(observations.Single().DeletedAtUtc);
        Assert.IsFalse(deleteFields.ContainsKey("TargetUserName_resolved"));
    }

    [TestMethod]
    public void Observe_UsesKqlProjectedEventPayloadCompanionFieldsWhenPresent()
    {
        var service = new WindowsSidObservationService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), new StubResolver());
        var fields = CreateSecurityFields(4732);
        fields["MemberSid"] = StubResolver.Sid;
        fields["MemberName"] = "event_name";

        var observations = service.Observe(CreateSource(fields), fields);

        Assert.AreEqual("event_name", observations.Single().ResolvedAccountName);
        Assert.AreEqual("EventPayload", observations.Single().ResolutionSource);
        Assert.AreEqual("event_name", fields["MemberName"]);
        Assert.IsFalse(fields.ContainsKey("MemberName_resolved"));
    }

    private static Dictionary<string, object?> CreateSecurityFields(int eventId) => new(StringComparer.OrdinalIgnoreCase) {
        ["EventId"] = eventId,
        ["ProviderName"] = "Microsoft-Windows-Security-Auditing",
        ["RecordId"] = "42",
        ["TimeCreated"] = "2026-07-02T00:00:00Z"
    };

    private static SourceEvent CreateSource(Dictionary<string, object?> fields) => new(
        new ResourceMetadata { SourceType = "WindowsEventLog", SourceName = "Security", CollectorId = "agent-1" },
        fields);

    private sealed class StubResolver : IWindowsSidResolver
    {
        public const string Sid = "S-1-5-21-111-222-333-1007";

        public SidResolutionResult Resolve(string sid, DateTimeOffset observedAtUtc, TimeSpan cacheTtl) =>
            new(sid, "lenovo_123456", "HOST01", "HOST01\\lenovo_123456", "User", "LocalMachine", "LookupAccountSidW", "Resolved", "High", observedAtUtc, observedAtUtc.Add(cacheTtl));
    }

    private sealed class FailingResolver : IWindowsSidResolver
    {
        public SidResolutionResult Resolve(string sid, DateTimeOffset observedAtUtc, TimeSpan cacheTtl) =>
            new(sid, null, null, null, "Unknown", "Unknown", "LookupAccountSidW", "NotMapped", "Unknown", observedAtUtc, observedAtUtc.Add(cacheTtl));
    }
}