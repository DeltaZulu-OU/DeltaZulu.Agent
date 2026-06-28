using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Ndjson;
using System.Text.Json;

namespace DeltaZulu.Pipeline.Outputs.Ndjson;

public sealed class ConsoleNdjsonSink : IOutputWriter
{
    private readonly JsonSerializerOptions _jsonOptions = NdjsonSerializerOptions.CreateDefault();
    public string Name { get; }

    public ConsoleNdjsonSink(string name = "ndjson-console")
    {
        Name = name;
    }

    public void OnNext(ResourceOutputRecord value) => Console.WriteLine(JsonSerializer.Serialize(value, _jsonOptions));

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
