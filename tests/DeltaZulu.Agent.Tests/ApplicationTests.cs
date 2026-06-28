using System.Reactive.Linq;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Agent.Pipeline;
using DeltaZulu.Agent.Runtime;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ApplicationTests
{
    [TestMethod]
    public void PipelineAssembly_DoesNotReferenceLegacyProjectsAndBcl()
    {
        var applicationAssembly = typeof(ResourcePipeline).Assembly;
        var referencedAssemblies = applicationAssembly.GetReferencedAssemblies();

        var projectReferences = referencedAssemblies
            .Where(name => name.Name!.StartsWith("DeltaZulu", StringComparison.Ordinal))
            .Select(name => name.Name!)
            .ToList();

        Assert.IsEmpty(projectReferences, $"Expected no DeltaZulu project references, found: {string.Join(", ", projectReferences)}");
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
        Assert.AreEqual(0, inner.Errors.Count);
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
        var metadata = new ResourceMetadata
        {
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
        var metadata = new ResourceMetadata
        {
            SourceType = "test",
            SourceName = "test",
            Platform = "test",
            CollectorId = "test",
            Hostname = "test"
        };
        return new SourceEvent(metadata, new Dictionary<string, object?>
        {
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
        public void OnNext(ResourceOutputRecord value) { }
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
        public void Dispose() { }
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
        public void Dispose() { }
    }

    public TestContext TestContext { get; set; }
}
