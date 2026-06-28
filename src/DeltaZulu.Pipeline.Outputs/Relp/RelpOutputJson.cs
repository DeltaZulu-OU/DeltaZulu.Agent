using System.Text.Json;
using DeltaZulu.Pipeline.Core.Ndjson;

namespace DeltaZulu.Pipeline.Outputs.Relp;

internal static class RelpOutputJson
{
    public static JsonSerializerOptions Options { get; } = NdjsonSerializerOptions.CreateDefault();
}
