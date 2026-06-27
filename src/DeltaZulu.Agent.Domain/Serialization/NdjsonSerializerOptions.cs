using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeltaZulu.Agent.Outputs.Ndjson;

public static class NdjsonSerializerOptions
{
    public static JsonSerializerOptions CreateDefault() => new JsonSerializerOptions()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    }.AddNdjsonConverters();
}

internal static class NdjsonSerializerOptionsExtensions
{
    public static JsonSerializerOptions AddNdjsonConverters(this JsonSerializerOptions options)
    {
        options.Converters.Add(new NdjsonObjectConverter());
        return options;
    }
}
