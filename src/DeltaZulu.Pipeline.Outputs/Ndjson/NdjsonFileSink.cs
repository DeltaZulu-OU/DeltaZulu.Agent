using System.Text.Json;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Ndjson;

namespace DeltaZulu.Pipeline.Outputs.Ndjson;

public sealed class NdjsonFileSink : IOutputWriter
{
    private readonly FileStream _stream;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Lock _lock = new();
    private bool _disposed;

    public string Name { get; }

    public NdjsonFileSink(string path, string name = "ndjson-file")
    {
        Name = name;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        _stream = new FileStream(path, new FileStreamOptions {
            Mode = FileMode.Append,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            BufferSize = 64 * 1024,
            Options = FileOptions.SequentialScan
        });
        _jsonOptions = NdjsonSerializerOptions.CreateDefault();
    }

    public void OnNext(ResourceOutputRecord value)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            JsonSerializer.Serialize(_stream, value, _jsonOptions);
            _stream.WriteByte((byte)'\n');
        }
    }

    public void OnError(Exception error)
    {
        var errorRecord = NdjsonErrorRecord.FromException(error);
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            JsonSerializer.Serialize(_stream, errorRecord, _jsonOptions);
            _stream.WriteByte((byte)'\n');
            _stream.Flush();
        }
    }

    public void OnCompleted() => Dispose();

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stream.Dispose();
        }
    }
}