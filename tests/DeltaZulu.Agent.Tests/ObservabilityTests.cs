using System.Reactive.Linq;
using DeltaZulu.Agent.Shared.Pipeline.Abstractions;
using DeltaZulu.Agent.Shared.Pipeline;
using DeltaZulu.Agent.Shared.Pipeline.Events;
using DeltaZulu.Agent.Shared.Pipeline.Observability;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ObservabilityTests
{
    [TestMethod]
    public void LogTelemetryKey_UsesSourceChannelProviderAndEventId()
    {
        var source = CreateWindowsEvent(4688, "Microsoft-Windows-Security-Auditing");

        var key = LogTelemetryKey.FromSourceEvent(source);

        Assert.AreEqual("WindowsEventLog", key.SourceType);
        Assert.AreEqual("Security", key.Channel);
        Assert.AreEqual("Microsoft-Windows-Security-Auditing", key.Provider);
        Assert.AreEqual(4688, key.EventId);
    }

    [TestMethod]
    public void PipelineCountsObservation_EmitsPlannedRecordKindAndFields()
    {
        var observation = new PipelineCountsObservation
        {
            LogKey = new LogTelemetryKey("WindowsEventLog", "Security", "Microsoft-Windows-Security-Auditing", 4688),
            Metadata = new CollectorObservationMetadata
            {
                AgentId = "endpoint-123",
                HostId = "host-abc",
                ProfileId = "windows-security-default",
                ObservedAt = DateTimeOffset.Parse("2026-06-25T12:00:00Z"),
                WindowStart = DateTimeOffset.Parse("2026-06-25T11:55:00Z"),
                WindowEnd = DateTimeOffset.Parse("2026-06-25T12:00:00Z")
            },
            ReadCount = 1200,
            KeptAfterFilterCount = 1100,
            DiscardedCount = 100,
            ForwardedCount = 1099,
            ForwardFailedCount = 1
        };

        var record = observation.ToOutputRecord();

        Assert.AreEqual(PipelineCountsObservation.RecordKind, record.Metadata["recordKind"]);
        Assert.AreEqual("endpoint-123", record.Metadata["agentId"]);
        Assert.AreEqual("host-abc", record.Metadata["hostId"]);
        Assert.AreEqual("windows-security-default", record.Metadata["profileId"]);
        Assert.AreEqual("WindowsEventLog", record.Event["sourceType"]);
        Assert.AreEqual("Security", record.Event["channel"]);
        Assert.AreEqual("Microsoft-Windows-Security-Auditing", record.Event["provider"]);
        Assert.AreEqual(4688, record.Event["eventId"]);
        Assert.AreEqual(1200L, record.Event["readCount"]);
        Assert.AreEqual(1100L, record.Event["keptAfterFilterCount"]);
        Assert.AreEqual(100L, record.Event["discardedCount"]);
        Assert.AreEqual(1099L, record.Event["forwardedCount"]);
        Assert.AreEqual(1L, record.Event["forwardFailedCount"]);
    }

    [TestMethod]
    public void ResourcePipeline_RecordsReadKeptDiscardedAndForwardedCounts()
    {
        var observations = new AgentObservationAccumulator();
        var input = new TestInput([
            CreateWindowsEvent(4688, "Microsoft-Windows-Security-Auditing"),
            CreateWindowsEvent(4688, "Microsoft-Windows-Security-Auditing")
        ]);
        using var sink = new TestSink();
        var pipeline = new ResourcePipeline(
            input,
            source => source.Where((_, index) => index == 0).Select(sourceEvent => ResourceOutputRecord.FromSource(sourceEvent)),
            sink,
            observations);

        using var subscription = pipeline.Start(TestContext.CancellationToken);

        var count = observations.SnapshotPipelineCounts(new CollectorObservationMetadata
        {
            AgentId = "agent",
            HostId = "host",
            ProfileId = "profile"
        }).Single();

        Assert.AreEqual(2, count.ReadCount);
        Assert.AreEqual(1, count.KeptAfterFilterCount);
        Assert.AreEqual(1, count.DiscardedCount);
        Assert.AreEqual(1, count.ForwardedCount);
        Assert.AreEqual(0, count.ForwardFailedCount);
    }


    [TestMethod]
    public void AgentObservationAccumulator_AggregatesOverflowKeys()
    {
        var observations = new AgentObservationAccumulator();

        for (var i = 0; i <= 10_000; i++)
        {
            observations.RecordRead(CreateWindowsEvent(i, $"provider-{i}"));
        }

        var counts = observations.SnapshotPipelineCounts(new CollectorObservationMetadata
        {
            AgentId = "agent",
            HostId = "host",
            ProfileId = "profile"
        });

        var overflow = counts.Single(count => count.LogKey.SourceType == "__overflow__");
        Assert.AreEqual(10_000, counts.Count);
        Assert.AreEqual(2, overflow.ReadCount);
    }

    private static SourceEvent CreateWindowsEvent(int eventId, string provider) => new(
        new ResourceMetadata
        {
            SourceType = "WindowsEventLog",
            SourceName = "Security",
            Platform = "windows",
            CollectorId = "endpoint-123",
            Hostname = "host-abc"
        },
        new Dictionary<string, object?>
        {
            ["EventId"] = eventId,
            ["ProviderName"] = provider
        });

    private sealed class TestInput : ISourceInput
    {
        private readonly IReadOnlyList<SourceEvent> _events;
        public TestInput(IReadOnlyList<SourceEvent> events)
        {
            _events = events;
        }

        public string Name => "test";
        public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default) => _events.ToObservable();
    }

    private sealed class TestSink : IOutputWriter
    {
        public string Name => "test";
        public List<ResourceOutputRecord> Records { get; } = [];
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
        public void OnNext(ResourceOutputRecord value) => Records.Add(value);
        public void Dispose() { }
    }

    public TestContext TestContext { get; set; }
}
