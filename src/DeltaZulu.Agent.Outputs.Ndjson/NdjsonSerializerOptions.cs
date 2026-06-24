using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeltaZulu.Agent.Outputs.Ndjson;

public static class NdjsonSerializerOptions
{
    public static JsonSerializerOptions CreateDefault() => new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null
    };
}