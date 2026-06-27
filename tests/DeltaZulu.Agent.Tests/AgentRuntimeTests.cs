using System.Reactive.Linq;
using DeltaZulu.Agent.Application.Abstractions;
using DeltaZulu.Agent.Application.Runtime;
using DeltaZulu.Agent.Core.Events;
using DeltaZulu.Agent.Profiles;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class AgentRuntimeTests
{
    [TestMethod]
    public void SingleBinding_ExecutesThroughPipeline()
    {
        var input = new TestInput([CreateEvent("a"), CreateEvent("b")]);
        var sink = new RecordingSink();
        var executor = new PassthroughExecutor();
        var binding = new ProfileBinding(input, CreateProfile(), executor);

        var runtime = new AgentRuntime([binding], sink);
        var result = runtime.Run(TestContext.CancellationToken);

        Assert.IsTrue(result.Success);
        Assert.IsNull(result.Error);
        Assert.HasCount(2, sink.Records);
    }

    [TestMethod]
    public void MultipleBindings_AllProfilesExecute()
    {
        var input1 = new TestInput([CreateEvent("a")]);
        var input2 = new TestInput([CreateEvent("b"), CreateEvent("c")]);
        var sink = new RecordingSink();
        var executor = new PassthroughExecutor();
        var bindings = new[]
        {
            new ProfileBinding(input1, CreateProfile("p1"), executor),
            new ProfileBinding(input2, CreateProfile("p2"), executor)
        };

        var runtime = new AgentRuntime(bindings, sink);
        var result = runtime.Run(TestContext.CancellationToken);

        Assert.IsTrue(result.Success);
        Assert.HasCount(3, sink.Records);
    }

    [TestMethod]
    public void NonMandatoryProfile_ErrorDoesNotFailRuntime()
    {
        var goodInput = new TestInput([CreateEvent("ok")]);
        var badInput = new FailingInput();
        var sink = new RecordingSink();
        var executor = new PassthroughExecutor();
        var bindings = new[]
        {
            new ProfileBinding(goodInput, CreateProfile("good"), executor),
            new ProfileBinding(badInput, CreateProfile("bad", mandatory: false), executor)
        };

        var runtime = new AgentRuntime(bindings, sink);
        var result = runtime.Run(TestContext.CancellationToken);

        Assert.IsTrue(result.Success);
        Assert.HasCount(1, sink.Records);
    }

    [TestMethod]
    public void MandatoryProfile_ErrorFailsRuntime()
    {
        var badInput = new FailingInput();
        var goodInput = new TestInput([CreateEvent("ok")]);
        var sink = new RecordingSink();
        var executor = new PassthroughExecutor();
        var bindings = new[]
        {
            new ProfileBinding(goodInput, CreateProfile("good"), executor),
            new ProfileBinding(badInput, CreateProfile("bad", mandatory: true), executor)
        };

        var runtime = new AgentRuntime(bindings, sink);
        var result = runtime.Run(TestContext.CancellationToken);

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void NonMandatoryProfile_WarnCallbackInvoked()
    {
        var badInput = new FailingInput();
        var goodInput = new TestInput([CreateEvent("ok")]);
        var sink = new RecordingSink();
        var executor = new PassthroughExecutor();
        var warnings = new List<string>();
        var bindings = new[]
        {
            new ProfileBinding(goodInput, CreateProfile("good"), executor),
            new ProfileBinding(badInput, CreateProfile("bad", mandatory: false), executor)
        };

        var runtime = new AgentRuntime(bindings, sink, warn: warnings.Add);
        var result = runtime.Run(TestContext.CancellationToken);

        Assert.IsTrue(result.Success);
        Assert.HasCount(1, warnings);
        Assert.Contains("bad", warnings[0]);
    }

    [TestMethod]
    public void SingleBinding_NonMandatory_ErrorDoesNotFailRuntime()
    {
        var badInput = new FailingInput();
        var sink = new RecordingSink();
        var executor = new PassthroughExecutor();
        var binding = new ProfileBinding(badInput, CreateProfile("solo", mandatory: false), executor);

        var runtime = new AgentRuntime([binding], sink);
        var result = runtime.Run(TestContext.CancellationToken);

        Assert.IsTrue(result.Success);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void SingleBinding_NonMandatory_WarnCallbackInvoked()
    {
        var badInput = new FailingInput();
        var sink = new RecordingSink();
        var executor = new PassthroughExecutor();
        var warnings = new List<string>();
        var binding = new ProfileBinding(badInput, CreateProfile("solo", mandatory: false), executor);

        var runtime = new AgentRuntime([binding], sink, warn: warnings.Add);
        var result = runtime.Run(TestContext.CancellationToken);

        Assert.IsTrue(result.Success);
        Assert.HasCount(1, warnings);
        Assert.Contains("solo", warnings[0]);
    }

    [TestMethod]
    public void Cancellation_PropagatesAllProfiles()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var input = new TestInput([CreateEvent("a")]);
        var sink = new RecordingSink();
        var executor = new PassthroughExecutor();
        var binding = new ProfileBinding(input, CreateProfile(), executor);

        var runtime = new AgentRuntime([binding], sink);
        Assert.ThrowsExactly<OperationCanceledException>(() => runtime.Run(cts.Token));
    }

    private static ResourceProfile CreateProfile(string id = "test", bool mandatory = true) => new() {
        SchemaVersion = 1,
        Id = id,
        Name = id,
        Version = "1.0.0",
        Mandatory = mandatory,
        Resource = new ResourceDescriptor { Platform = "test", Family = "test" },
        Input = new ResourceInputContract { Table = "Source" },
        Output = new ResourceOutputContract { Format = "ndjson" }
    };

    private static SourceEvent CreateEvent(string label) => new(
        new ResourceMetadata {
            SourceType = "test",
            SourceName = "test",
            Platform = "test",
            CollectorId = "test",
            Hostname = "test"
        },
        new Dictionary<string, object?> { ["label"] = label });

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

    private sealed class FailingInput : ISourceInput
    {
        public string Name => "failing";
        public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default) =>
            Observable.Throw<SourceEvent>(new InvalidOperationException("input failed"));
    }

    private sealed class PassthroughExecutor : IProfileExecutor
    {
        public IObservable<ResourceOutputRecord> Execute(
            IObservable<SourceEvent> source,
            ResourceProfile profile,
            CancellationToken cancellationToken = default) =>
            source.Select( o => ResourceOutputRecord.FromSource(o, profile.Id, profile.Version));

        public void Dispose() { }
    }

    private sealed class RecordingSink : IOutputWriter
    {
        public string Name => "recording";
        public List<ResourceOutputRecord> Records { get; } = [];
        public void OnNext(ResourceOutputRecord value) => Records.Add(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
        public void Dispose() { }
    }

    public TestContext TestContext { get; set; }
}
