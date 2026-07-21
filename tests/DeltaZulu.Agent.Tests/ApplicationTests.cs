using System.Reactive.Linq;
using DeltaZulu.Pipeline.Core;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ApplicationTests
{
    // Allowlist, not a blocklist: any accidental Agent-layer reference (Runtime, Daemon,
    // Cli, ProfileWorkbench, Filter) fails this test because it is not in this set, which
    // is how ROADMAP.md Phase 1's "dependency tests reject Agent references" requirement
    // is satisfied without enumerating every Agent-layer assembly name separately.
    private static readonly string[] AllowedPipelineDeltaZuluDependencies =
    [
        "DeltaZulu.DurableBuffer",
        "DeltaZulu.Transport",
        "DeltaZulu.Parse",
        "DeltaZulu.LocalStream",
        "DeltaZulu.Forward"
    ];

    [TestMethod]
    public void PipelineAssembly_ReferencesOnlyExternalPipelineDependencies()
    {
        var applicationAssembly = typeof(ResourcePipeline).Assembly;
        var referencedAssemblies = applicationAssembly.GetReferencedAssemblies();

        var disallowedProjectReferences = referencedAssemblies
            .Where(name => name.Name!.StartsWith("DeltaZulu", StringComparison.Ordinal) && !AllowedPipelineDeltaZuluDependencies.Contains(name.Name))
            .Select(name => name.Name!)
            .ToList();

        Assert.IsEmpty(disallowedProjectReferences,
            $"Pipeline must not reference legacy or Agent-layer DeltaZulu projects: {string.Join(", ", disallowedProjectReferences)}");
    }

    [TestMethod]
    public void PipelineAssembly_TransitionalDirectDurableBufferReferenceIsTracked()
    {
        var referencedNames = typeof(ResourcePipeline).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name!)
            .ToList();

        // ADR 0005/0008 and ROADMAP.md Phase 1/6: a direct DeltaZulu.DurableBuffer reference
        // is allowed only as a transitional detail until the LocalStream migration lands.
        // This assertion exists so its removal is a deliberate, documented step: once Phase 6
        // lands, this should fail, and the fix is to delete this test and the transitional
        // ProjectReference comment in DeltaZulu.Pipeline.csproj, not to re-add the reference.
        Assert.Contains("DeltaZulu.DurableBuffer", referencedNames);
    }

    [TestMethod]
    public void CompletionTrackingWriter_SignalsCompletionOnOnCompleted()
    {
        using var completed = new ManualResetEventSlim(false);
        var inner = new RecordingSink();
        using var writer = new CompletionTrackingWriter(inner, completed);

        writer.OnCompleted();

        Assert.IsTrue(completed.IsSet);
        Assert.IsTrue(inner.CompletedCalled);
        Assert.IsNull(writer.Error);
    }

    [TestMethod]
    public void CompletionTrackingWriter_SignalsCompletionOnError()
    {
        using var completed = new ManualResetEventSlim(false);
        var inner = new RecordingSink();
        using var writer = new CompletionTrackingWriter(inner, completed);
        var exception = new InvalidOperationException("test error");

        writer.OnError(exception);

        Assert.IsTrue(completed.IsSet);
        Assert.AreSame(exception, writer.Error);
    }

    [TestMethod]
    public void CompletionTrackingWriter_SignalsCompletionWhenInnerOnCompletedThrows()
    {
        using var completed = new ManualResetEventSlim(false);
        var inner = new ThrowingSink(throwOnCompleted: true);
        using var writer = new CompletionTrackingWriter(inner, completed);

        Assert.ThrowsExactly<InvalidOperationException>(writer.OnCompleted);
        Assert.IsTrue(completed.IsSet);
    }

    [TestMethod]
    public void CompletionTrackingWriter_SignalsCompletionWhenInnerOnErrorThrows()
    {
        using var completed = new ManualResetEventSlim(false);
        var inner = new ThrowingSink(throwOnError: true);
        using var writer = new CompletionTrackingWriter(inner, completed);
        var exception = new InvalidOperationException("test error");

        Assert.ThrowsExactly<InvalidOperationException>(() => writer.OnError(exception));

        Assert.IsTrue(completed.IsSet);
        Assert.AreSame(exception, writer.Error);
    }

    [TestMethod]
    public void CompletionTrackingWriter_ForwardsRecordsToInner()
    {
        using var completed = new ManualResetEventSlim(false);
        var inner = new RecordingSink();
        using var writer = new CompletionTrackingWriter(inner, completed);
        var record = CreateRecord();

        writer.OnNext(record);
        writer.OnCompleted();

        Assert.HasCount(1, inner.Records);
        Assert.AreSame(record, inner.Records[0]);
    }

    [TestMethod]
    public void CompletionTrackingWriter_DoesNotForwardErrorToInnerWhenCompleteInnerIsFalse()
    {
        using var completed = new ManualResetEventSlim(false);
        var inner = new RecordingSink();
        using var writer = new CompletionTrackingWriter(inner, completed, completeInner: false);
        var exception = new InvalidOperationException("test error");

        writer.OnError(exception);

        Assert.IsTrue(completed.IsSet);
        Assert.AreSame(exception, writer.Error);
        Assert.IsEmpty(inner.Errors);
    }

    [TestMethod]
    public void CompletionTrackingWriter_DoesNotCompleteInnerWhenFlagIsFalse()
    {
        using var completed = new ManualResetEventSlim(false);
        var inner = new RecordingSink();
        using var writer = new CompletionTrackingWriter(inner, completed, completeInner: false);

        writer.OnCompleted();

        Assert.IsTrue(completed.IsSet);
        Assert.IsFalse(inner.CompletedCalled);
    }

    [TestMethod]
    public void ChannelOutputMultiplexer_SerializesConcurrentWritesToInner()
    {
        var inner = new RecordingSink();
        using var mux = new ChannelOutputMultiplexer(inner);
        var records = Enumerable.Range(0, 100)
            .Select(i => CreateRecord($"event-{i}"))
            .ToList();

        Parallel.ForEach(records, record => mux.OnNext(record));
        mux.Complete();

        Assert.HasCount(100, inner.Records);
    }

    [TestMethod]
    public void ChannelOutputMultiplexer_PropagatesErrorToInner()
    {
        var inner = new RecordingSink();
        using var mux = new ChannelOutputMultiplexer(inner);
        var exception = new InvalidOperationException("test error");

        mux.OnError(exception);
        mux.Complete();

        Assert.AreSame(exception, mux.Error);
        Assert.HasCount(1, inner.Errors);
    }

    [TestMethod]
    public void ChannelOutputMultiplexer_ErrorTerminatesTheWriter()
    {
        var inner = new RecordingSink();
        using var mux = new ChannelOutputMultiplexer(inner);

        mux.OnError(new InvalidOperationException("test error"));

        Assert.ThrowsExactly<InvalidOperationException>(() => mux.OnNext(CreateRecord()));
        mux.Complete();

        Assert.IsFalse(inner.CompletedCalled);
    }

    [TestMethod]
    public void ResourcePipeline_WiresInputThroughTransformToSink()
    {
        var input = new TestInput([
            CreateSourceEvent("evt-1"),
            CreateSourceEvent("evt-2"),
            CreateSourceEvent("evt-3")
        ]);
        var sink = new RecordingSink();
        var pipeline = new ResourcePipeline(
            input,
            source => source.Select(o => ResourceOutputRecord.FromSource(o)),
            sink);

        using var subscription = pipeline.Start(TestContext.CancellationToken);

        Assert.HasCount(3, sink.Records);
    }

    [TestMethod]
    public void ResourcePipeline_FilterReducesOutput()
    {
        var input = new TestInput([
            CreateSourceEvent("keep"),
            CreateSourceEvent("drop"),
            CreateSourceEvent("keep")
        ]);
        var sink = new RecordingSink();
        var pipeline = new ResourcePipeline(
            input,
            source => source
                .Where(e => e.Fields.ContainsKey("keep") && (bool)e.Fields["keep"]!)
                .Select(o => ResourceOutputRecord.FromSource(o)),
            sink);

        using var subscription = pipeline.Start(TestContext.CancellationToken);

        Assert.HasCount(2, sink.Records);
    }

    private static ResourceOutputRecord CreateRecord(string id = "test")
    {
        var metadata = new ResourceMetadata {
            SourceType = "test",
            SourceName = "test",
            Platform = "test",
            CollectorId = "test",
            Hostname = "test"
        };
        return new ResourceOutputRecord() with { Metadata = metadata.ToDictionary().AsReadOnly(), Enrichment = new Dictionary<string, object?> { ["id"] = id } };
    }

    private static SourceEvent CreateSourceEvent(string label)
    {
        var metadata = new ResourceMetadata {
            SourceType = "test",
            SourceName = "test",
            Platform = "test",
            CollectorId = "test",
            Hostname = "test"
        };
        return new SourceEvent(metadata, new Dictionary<string, object?> {
            ["label"] = label,
            ["keep"] = label == "keep"
        });
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

    private sealed class ThrowingSink : IOutputWriter
    {
        private readonly bool _throwOnCompleted;
        private readonly bool _throwOnError;

        public ThrowingSink(bool throwOnCompleted = false, bool throwOnError = false)
        {
            (_throwOnCompleted, _throwOnError) = (throwOnCompleted, throwOnError);
        }

        public string Name => "throwing";

        public void OnNext(ResourceOutputRecord value)
        { }

        public void OnError(Exception error)
        {
            if (_throwOnError)
            {
                throw new InvalidOperationException("inner error failed");
            }
        }

        public void OnCompleted()
        {
            if (_throwOnCompleted)
            {
                throw new InvalidOperationException("inner completion failed");
            }
        }

        public void Dispose()
        { }
    }

    private sealed class RecordingSink : IOutputWriter
    {
        public string Name => "recording";
        public List<ResourceOutputRecord> Records { get; } = [];
        public List<Exception> Errors { get; } = [];
        public bool CompletedCalled { get; private set; }

        public void OnNext(ResourceOutputRecord value) => Records.Add(value);

        public void OnError(Exception error) => Errors.Add(error);

        public void OnCompleted() => CompletedCalled = true;

        public void Dispose()
        { }
    }

    public TestContext TestContext { get; set; }
}
