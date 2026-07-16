using System.Text.Json;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Ndjson;

namespace DeltaZulu.Pipeline.Outputs.Ndjson;

public sealed class ConsoleNdjsonSink : IOutputWriter
{
    private readonly JsonSerializerOptions _jsonOptions;

    public ConsoleNdjsonSink(string name = "ndjson-console", bool prettyPrint = false)
    {
        Name = name;
        _jsonOptions = NdjsonSerializerOptions.CreateDefault();
        _jsonOptions.WriteIndented = prettyPrint;
    }

    public string Name { get; }

    public void Dispose()
    { }

    public void OnCompleted()
    { }

    public void OnError(Exception error)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(NdjsonErrorRecord.FromException(error), _jsonOptions));
        Console.Error.Flush();
    }

    public void OnNext(ResourceOutputRecord value) => Console.WriteLine(JsonSerializer.Serialize(value, _jsonOptions));
}
