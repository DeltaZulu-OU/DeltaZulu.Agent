using System.Text.Json;
using DeltaZulu.Buffer.Abstractions;

namespace DeltaZulu.Buffer;

public sealed class JsonRecordSerializer<T> : IRecordSerializer<T>
{
    private readonly JsonSerializerOptions? _options;

    public JsonRecordSerializer(JsonSerializerOptions? options = null) => _options = options;

    public ReadOnlyMemory<byte> Serialize(T record) =>
        JsonSerializer.SerializeToUtf8Bytes(record, _options);
}