using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using System.Text.Json;

namespace DeltaZulu.Agent.Outputs.Ndjson;

public sealed class ConsoleNdjsonSink : IResourceSink
{
    private readonly JsonSerializerOptions _jsonOptions = NdjsonSerializerOptions.CreateDefault();
    public string Name { get; }

    public ConsoleNdjsonSink(string name = "ndjson-console")
    {
        Name = name;
    }

    public void OnNext(ResourceOutputRecord value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, _jsonOptions));
    }

    public void OnError(Exception error)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(NdjsonErrorRecord.FromException(error), _jsonOptions));
        Console.Error.Flush();
    }

    public void OnCompleted()
    { }

    public void Dispose()
    { }
}
