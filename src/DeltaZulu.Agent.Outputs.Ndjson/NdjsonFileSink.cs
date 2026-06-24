using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using System.Text.Json;

namespace DeltaZulu.Agent.Outputs.Ndjson;

public sealed class NdjsonFileSink : IResourceSink
{
    private readonly StreamWriter _writer;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _lock = new();
    private bool _disposed;

    public string Name { get; }

    public NdjsonFileSink(string path, string name = "ndjson-file")
    {
        Name = name;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read));
        _jsonOptions = NdjsonSerializerOptions.CreateDefault();
    }

    public void OnNext(ResourceOutputRecord value)
    {
        lock (_lock)
        {
            if (_disposed) return;

            _writer.WriteLine(JsonSerializer.Serialize(value, _jsonOptions));
            _writer.Flush();
        }
    }

    public void OnError(Exception error)
    {
        var errorRecord = NdjsonErrorRecord.FromException(error);
        lock (_lock)
        {
            if (_disposed) return;

            _writer.WriteLine(JsonSerializer.Serialize(errorRecord, _jsonOptions));
            _writer.Flush();
        }
    }

    public void OnCompleted() => Dispose();

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;

            _disposed = true;
            _writer.Dispose();
        }
    }
}