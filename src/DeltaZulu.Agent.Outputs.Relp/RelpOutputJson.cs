using System.Text.Json;
using DeltaZulu.Agent.Outputs.Ndjson;

namespace DeltaZulu.Agent.Outputs.Relp;

internal static class RelpOutputJson
{
    public static JsonSerializerOptions Options { get; } = NdjsonSerializerOptions.CreateDefault();
}
