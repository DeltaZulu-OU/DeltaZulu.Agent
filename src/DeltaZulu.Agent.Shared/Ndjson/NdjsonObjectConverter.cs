using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeltaZulu.Agent.Shared.Ndjson;

internal sealed class NdjsonObjectConverter : JsonConverter<object?>
{
    private const string SecurityIdentifierTypeName = "System.Security.Principal.SecurityIdentifier";

    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<JsonElement>(ref reader, options);

    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.GetType().FullName == SecurityIdentifierTypeName)
        {
            writer.WriteStringValue(value.ToString());
            return;
        }

        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
