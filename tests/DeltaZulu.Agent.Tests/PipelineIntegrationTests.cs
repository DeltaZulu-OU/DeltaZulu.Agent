using System.Reactive.Linq;
using System.Text.Json;
using DeltaZulu.Agent.Runtime;
using DeltaZulu.Pipeline.Core;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Delivery;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Ndjson;
using DeltaZulu.Pipeline.Core.Observability;
using DeltaZulu.Pipeline.Core.Profiles;
using DeltaZulu.Pipeline.Enrichment.Events;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class PipelineIntegrationTests
{
    [TestMethod]
    public void EndToEnd_InputThroughExecutorToSink()
    {
        var events = Enumerable.Range(1, 5)
            .Select(i => CreateEvent($"evt-{i}"))
            .ToList();
        var input = new TestInput(events);
        var sink = new RecordingSink();
        var executor = new PassthroughExecutor();
        var binding = new ProfileBinding(input, CreateProfile(), executor);

        var runtime = new AgentRuntime([binding], sink);
        var result = runtime.Run(TestContext.CancellationToken);

        Assert.IsTrue(result.Success);
        Assert.HasCount(5, sink.Records);
        for (var i = 0; i < 5; i++)
        {
            Assert.AreEqual($"evt-{i + 1}", sink.Records[i].Event["label"]);
        }
    }

    [TestMethod]
    public void EndToEnd_WithObservationAccumulator()
    {
        var events = new[]
        {
            CreateEvent("a", eventId: 100),
            CreateEvent("b", eventId: 100),
            CreateEvent("c", eventId: 200)
        };
        var input = new TestInput(events);
        var sink = new RecordingSink();
        var observations = new AgentObservationAccumulator();
        var executor = new PassthroughExecutor();
        var binding = new ProfileBinding(input, CreateProfile(), executor);

        var runtime = new AgentRuntime([binding], sink, observations);
        var result = runtime.Run(TestContext.CancellationToken);

        Assert.IsTrue(result.Success);
        Assert.HasCount(3, sink.Records);

        var counts = observations.SnapshotPipelineCounts(new CollectorObservationMetadata {
            AgentId = "test-agent",
            HostId = "test-host"
        });
        Assert.HasCount(2, counts);
        Assert.AreEqual(2, counts[0].ReadCount);
        Assert.AreEqual(1, counts[1].ReadCount);
    }

    [TestMethod]
    public void EndToEnd_MultiProfile_InterleavedOutput()
    {
        var input1 = new TestInput([CreateEvent("from-p1-a"), CreateEvent("from-p1-b")]);
        var input2 = new TestInput([CreateEvent("from-p2-a")]);
        var sink = new RecordingSink();
        var executor = new PassthroughExecutor();
        var bindings = new[]
        {
            new ProfileBinding(input1, CreateProfile("profile-1"), executor),
            new ProfileBinding(input2, CreateProfile("profile-2"), executor)
        };

        var runtime = new AgentRuntime(bindings, sink);
        var result = runtime.Run(TestContext.CancellationToken);

        Assert.IsTrue(result.Success);
        Assert.HasCount(3, sink.Records);
    }

    [TestMethod]
    public void DeliveryRecord_JsonRoundTrip()
    {
        var metadata = new Dictionary<string, object?> {
            ["collectorId"] = "agent-1",
            ["sourceType"] = "syslog",
            ["sourceName"] = "auth.log"
        };
        var eventData = new Dictionary<string, object?> { ["Message"] = "test message" };
        var output = new ResourceOutputRecord { Metadata = metadata, Event = eventData };
        var original = DeliveryRecord.FromResourceOutput(output);

        var options = NdjsonSerializerOptions.CreateDefault();
        var json = JsonSerializer.Serialize(original, options);
        var roundTripped = JsonSerializer.Deserialize<DeliveryRecord>(json, options);

        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(original.AgentId, roundTripped.AgentId);
        Assert.AreEqual(original.SourceId, roundTripped.SourceId);
        Assert.AreEqual(original.DeliveryId, roundTripped.DeliveryId);
    }

    [TestMethod]
    public void ResourceOutputRecord_JsonRoundTrip()
    {
        var record = new ResourceOutputRecord {
            Metadata = new Dictionary<string, object?> { ["sourceType"] = "test" },
            Event = new Dictionary<string, object?> {
                ["EventId"] = 4688,
                ["Message"] = "Process Created"
            }
        };

        var options = NdjsonSerializerOptions.CreateDefault();
        var json = JsonSerializer.Serialize(record, options);
        var roundTripped = JsonSerializer.Deserialize<ResourceOutputRecord>(json, options);

        Assert.IsNotNull(roundTripped);
        Assert.AreEqual("test", roundTripped.Metadata["sourceType"]?.ToString());
    }

    [TestMethod]
    public void ResourcePipeline_ObservableCompletionDisposesCleanly()
    {
        var input = new TestInput([CreateEvent("single")]);
        var sink = new RecordingSink();
        var pipeline = new ResourcePipeline(
            input,
            source => source.Select(o => ResourceOutputRecord.FromSource(o)),
            sink);

        using (var sub = pipeline.Start(TestContext.CancellationToken))
        {
        }

        Assert.HasCount(1, sink.Records);
        Assert.IsTrue(sink.Disposed);
    }

    private static ResourceProfile CreateProfile(string id = "test") => new() {
        SchemaVersion = 1,
        Id = id,
        Name = id,
        Version = "1.0.0",
        Resource = new ResourceDescriptor { Platform = "test", Family = "test" },
        Input = new ResourceInputContract { Table = "Source" },
        Output = new ResourceOutputContract { Format = "ndjson" }
    };

    private static SourceEvent CreateEvent(string label, int eventId = 1) => new(
        new ResourceMetadata {
            SourceType = "test",
            SourceName = "test",
            Platform = "test",
            CollectorId = "test-agent",
            Hostname = "test-host"
        },
        new Dictionary<string, object?> {
            ["label"] = label,
            ["EventId"] = eventId,
            ["ProviderName"] = "TestProvider"
        });


    [TestMethod]
    public void ResourcePipeline_AppliesRpcEnrichmentAfterFilterBeforeSink()
    {
        var input = new TestInput([
            new SourceEvent(
                new ResourceMetadata { SourceType = "WindowsEtw", SourceName = "Microsoft-Windows-RPC", RawPreserved = true },
                new Dictionary<string, object?>
                {
                    ["ProviderName"] = "Microsoft-Windows-RPC",
                    ["InterfaceUuid"] = "367abb81-9844-35f1-ad32-98f038001003",
                    ["ProcNum"] = 12,
                    ["NetworkAddress"] = "192.168.10.25"
                })
        ]);
        var sink = new RecordingSink();
        var sawPreOutputRecord = false;
        var pipeline = new ResourcePipeline(
            input,
            source => source.Select(sourceEvent => {
                var output = ResourceOutputRecord.FromSource(sourceEvent, "windows.etw.rpc.p0", "1.1.0");
                Assert.IsNull(output.Enrichment, "Profile/filter output should not be enriched before the post-filter enrichment stage.");
                sawPreOutputRecord = true;
                return output;
            }),
            sink,
            enrichAfterFilter: ResourceOutputEnricher.EnrichAfterFilter);

        using var subscription = pipeline.Start(TestContext.CancellationToken);

        Assert.IsTrue(sawPreOutputRecord);
        Assert.HasCount(1, sink.Records);
        Assert.IsNotNull(sink.Records[0].Enrichment);
        var rpc = (IReadOnlyDictionary<string, object?>)sink.Records[0].Enrichment!["Rpc"]!;
        Assert.AreEqual("RCreateServiceW", rpc["OperationName"]);
    }

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

    private sealed class PassthroughExecutor : IProfileExecutor
    {
        public IObservable<ResourceOutputRecord> Execute(
            IObservable<SourceEvent> source,
            ResourceProfile profile,
            CancellationToken cancellationToken = default) =>
            source.Select(o => ResourceOutputRecord.FromSource(o, profile.Id, profile.Version));

        public void Dispose()
        { }
    }

    private sealed class RecordingSink : IOutputWriter
    {
        public string Name => "recording";
        public List<ResourceOutputRecord> Records { get; } = [];
        public bool Disposed { get; private set; }

        public void OnNext(ResourceOutputRecord value) => Records.Add(value);

        public void OnError(Exception error)
        { }

        public void OnCompleted()
        { }

        public void Dispose() => Disposed = true;
    }

    public TestContext TestContext { get; set; }
}