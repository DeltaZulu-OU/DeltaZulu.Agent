using System.Reactive.Linq;
using System.Reactive.Subjects;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Agent.Runtime;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;

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
    public void SingleBinding_NonMandatory_StartupExceptionDoesNotFailRuntime()
    {
        var badInput = new ThrowingOpenInput();
        var sink = new RecordingSink();
        var executor = new PassthroughExecutor();
        var warnings = new List<string>();
        var binding = new ProfileBinding(badInput, CreateProfile("solo", mandatory: false), executor);

        var runtime = new AgentRuntime([binding], sink, warn: warnings.Add);
        var result = runtime.Run(TestContext.CancellationToken);

        Assert.IsTrue(result.Success);
        Assert.IsNull(result.Error);
        Assert.HasCount(1, warnings);
        Assert.Contains("solo", warnings[0]);
    }

    [TestMethod]
    public void SingleBinding_Mandatory_StartupExceptionFailsRuntime()
    {
        var badInput = new ThrowingOpenInput();
        var sink = new RecordingSink();
        var executor = new PassthroughExecutor();
        var binding = new ProfileBinding(badInput, CreateProfile("solo", mandatory: true), executor);

        var runtime = new AgentRuntime([binding], sink);
        var result = runtime.Run(TestContext.CancellationToken);

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Error);
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

    [TestMethod]
    public void ReloadableProfile_UsesNotifiedProfileForSubsequentEvents()
    {
        using var source = new Subject<SourceEvent>();
        var input = new ObservableInput(source);
        var sink = new RecordingSink();
        var executor = new PassthroughExecutor();
        var initialProfile = CreateProfile("initial");
        var reloads = new ProfileReloadSource(initialProfile);
        var binding = new ProfileBinding(input, initialProfile, executor, reloads);
        var runtime = new AgentRuntime([binding], sink);

        var runTask = Task.Run(() => runtime.Run(TestContext.CancellationToken));
        Assert.IsTrue(input.Opened.Wait(TimeSpan.FromSeconds(5), TestContext.CancellationToken));
        source.OnNext(CreateEvent("before"));
        Assert.IsTrue(SpinWait.SpinUntil(() => sink.Records.Count == 1, TimeSpan.FromSeconds(5)));

        reloads.NotifyProfileChanged(CreateProfile("replacement"));
        source.OnNext(CreateEvent("after"));
        source.OnCompleted();

        Assert.IsTrue(runTask.Wait(TimeSpan.FromSeconds(5), TestContext.CancellationToken));
        Assert.IsTrue(runTask.Result.Success);
        Assert.HasCount(2, sink.Records);
        Assert.AreEqual("initial", sink.Records[0].Metadata["profileId"]);
        Assert.AreEqual("replacement", sink.Records[1].Metadata["profileId"]);
    }

    [TestMethod]
    public void ReloadableProfile_NotificationWaitsForInFlightEventBeforeSwap()
    {
        using var source = new Subject<SourceEvent>();
        var input = new ObservableInput(source);
        var sink = new RecordingSink();
        var executor = new BlockingFirstEventExecutor();
        var initialProfile = CreateProfile("initial");
        var reloads = new ProfileReloadSource(initialProfile);
        var binding = new ProfileBinding(input, initialProfile, executor, reloads);
        var runtime = new AgentRuntime([binding], sink);

        var runTask = Task.Run(() => runtime.Run(TestContext.CancellationToken));
        Assert.IsTrue(input.Opened.Wait(TimeSpan.FromSeconds(5), TestContext.CancellationToken));

        var firstWrite = Task.Run(() => source.OnNext(CreateEvent("before")), TestContext.CancellationToken);
        Assert.IsTrue(executor.FirstEventStarted.Wait(TimeSpan.FromSeconds(5), TestContext.CancellationToken));

        var reloadTask = Task.Run(() => reloads.NotifyProfileChanged(CreateProfile("replacement")), TestContext.CancellationToken);
        Assert.IsFalse(reloadTask.Wait(TimeSpan.FromMilliseconds(100), TestContext.CancellationToken));

        executor.ReleaseFirstEvent.Set();
        Assert.IsTrue(firstWrite.Wait(TimeSpan.FromSeconds(5), TestContext.CancellationToken));
        Assert.IsTrue(reloadTask.Wait(TimeSpan.FromSeconds(5), TestContext.CancellationToken));

        source.OnNext(CreateEvent("after"));
        source.OnCompleted();

        Assert.IsTrue(runTask.Wait(TimeSpan.FromSeconds(5), TestContext.CancellationToken));
        Assert.IsTrue(runTask.Result.Success);
        Assert.HasCount(2, sink.Records);
        Assert.AreEqual("initial", sink.Records[0].Metadata["profileId"]);
        Assert.AreEqual("replacement", sink.Records[1].Metadata["profileId"]);
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

    private sealed class ObservableInput : ISourceInput
    {
        private readonly IObservable<SourceEvent> _source;

        public ObservableInput(IObservable<SourceEvent> source)
        {
            _source = source;
        }

        public string Name => "observable";
        public ManualResetEventSlim Opened { get; } = new(false);

        public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default)
        {
            Opened.Set();
            return _source;
        }
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

    private sealed class FailingInput : ISourceInput
    {
        public string Name => "failing";
        public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default) =>
            Observable.Throw<SourceEvent>(new InvalidOperationException("input failed"));
    }


    private sealed class ThrowingOpenInput : ISourceInput
    {
        public string Name => "throwing";
        public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("input open failed");
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

    private sealed class BlockingFirstEventExecutor : IProfileExecutor
    {
        public ManualResetEventSlim FirstEventStarted { get; } = new(false);
        public ManualResetEventSlim ReleaseFirstEvent { get; } = new(false);
        private int _eventCount;

        public IObservable<ResourceOutputRecord> Execute(
            IObservable<SourceEvent> source,
            ResourceProfile profile,
            CancellationToken cancellationToken = default) =>
            source.SelectMany(sourceEvent => Observable.Create<ResourceOutputRecord>(observer =>
            {
                if (Interlocked.Increment(ref _eventCount) == 1)
                {
                    FirstEventStarted.Set();
                    ReleaseFirstEvent.Wait(cancellationToken);
                }

                observer.OnNext(ResourceOutputRecord.FromSource(sourceEvent, profile.Id, profile.Version));
                observer.OnCompleted();
                return () => { };
            }));

        public void Dispose() { }
    }

    private sealed class RecordingSink : IOutputWriter
    {
        private readonly Lock _lock = new();
        private readonly List<ResourceOutputRecord> _records = [];

        public string Name => "recording";
        public IReadOnlyList<ResourceOutputRecord> Records
        {
            get
            {
                lock (_lock)
                {
                    return [.. _records];
                }
            }
        }

        public void OnNext(ResourceOutputRecord value)
        {
            lock (_lock)
            {
                _records.Add(value);
            }
        }
        public void OnError(Exception error) { }
        public void OnCompleted() { }
        public void Dispose() { }
    }

    public TestContext TestContext { get; set; }
}
