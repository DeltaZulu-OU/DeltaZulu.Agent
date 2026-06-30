using System.Text.Json;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Ndjson;
using DeltaZulu.Pipeline.Outputs.Ndjson;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class NdjsonTests
{
    [TestMethod]
    public void CreateDefault_PreservesPropertyNamesAndOmitsNullValues()
    {
        var json = JsonSerializer.Serialize(new { OriginalName = "value", Missing = (string?)null }, NdjsonSerializerOptions.CreateDefault());

        Assert.Contains("\"OriginalName\":\"value\"", json);
        Assert.IsFalse(json.Contains("Missing", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains(Environment.NewLine, StringComparison.Ordinal));
    }

    [TestMethod]
    public void CreateDefault_IgnoresObjectCycles()
    {
        var node = new CyclicNode();
        node.Next = node;

        var record = new ResourceOutputRecord {
            Event = new Dictionary<string, object?> {
                ["CyclicValue"] = node
            }
        };

        var json = JsonSerializer.Serialize(record, NdjsonSerializerOptions.CreateDefault());

        Assert.Contains("\"CyclicValue\"", json);
        Assert.Contains("\"Name\":\"root\"", json);
    }

    private sealed class CyclicNode
    {
        public string Name { get; init; } = "root";
        public CyclicNode? Next { get; set; }
    }

    [TestMethod]
    public void NdjsonFileSink_DisposeIsIdempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deltazulu-{Guid.NewGuid():N}.ndjson");
        try
        {
            using var sink = new NdjsonFileSink(path);

            sink.Dispose();
            sink.Dispose();
            sink.OnNext(new ResourceOutputRecord());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod]
    public void ConsoleNdjsonSink_PrettyPrintsWhenRequested()
    {
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            using var sink = new ConsoleNdjsonSink(prettyPrint: true);

            sink.OnNext(new ResourceOutputRecord {
                Metadata = new Dictionary<string, object?> { ["collectorId"] = "agent-01" },
                Event = new Dictionary<string, object?> { ["message"] = "hello" }
            });
        }
        finally
        {
            Console.SetOut(originalOutput);
        }

        var json = output.ToString();
        Assert.Contains(Environment.NewLine + "  \"_metadata\"", json);
        Assert.Contains(Environment.NewLine + "  \"event\"", json);
    }

    [TestMethod]
    public void ConsoleNdjsonSink_DefaultsToSingleLineJson()
    {
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        Console.SetOut(output);
        try
        {
            using var sink = new ConsoleNdjsonSink();

            sink.OnNext(new ResourceOutputRecord {
                Metadata = new Dictionary<string, object?> { ["collectorId"] = "agent-01" },
                Event = new Dictionary<string, object?> { ["message"] = "hello" }
            });
        }
        finally
        {
            Console.SetOut(originalOutput);
        }

        var json = output.ToString().TrimEnd();
        Assert.IsFalse(json.Contains(Environment.NewLine, StringComparison.Ordinal));
    }

    [TestMethod]
    public void FromException_MapsExceptionDetailsIntoErrorRecord()
    {
        var exception = new InvalidOperationException("boom");

        var record = NdjsonErrorRecord.FromException(exception);

        Assert.AreEqual("agent.error", record.Type);
        Assert.AreEqual("boom", record.Message);
        Assert.AreEqual(typeof(InvalidOperationException).FullName, record.ExceptionType);
    }
}