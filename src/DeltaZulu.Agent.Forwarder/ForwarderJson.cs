using System.Text.Json;
using DeltaZulu.Agent.Outputs.Ndjson;

namespace DeltaZulu.Agent.Forwarder;

internal static class ForwarderJson
{
    public static JsonSerializerOptions Options { get; } = NdjsonSerializerOptions.CreateDefault();
}
