using System.Text.Json;
using DeltaZulu.Pipeline.Core.Ndjson;

namespace DeltaZulu.Pipeline.Outputs.Forwarder;

internal static class ForwarderOutputJson
{
    public static JsonSerializerOptions Options { get; } = NdjsonSerializerOptions.CreateDefault();
}
