using DeltaZulu.Agent.Core.Events;

namespace DeltaZulu.Agent.Inputs.Windows;

internal static class WindowsSourceEventMapper
{
    public static SourceEvent FromDictionary(
        IReadOnlyDictionary<string, object?> fields,
        string sourceType,
        string sourceName,
        string parserName)
    {
        var hostname = TryGet(fields, "MachineName") ?? TryGet(fields, "Computer") ?? Environment.MachineName;
        var metadata = new ResourceMetadata
        {
            SourceType = sourceType,
            SourceName = sourceName,
            Platform = "windows",
            Hostname = hostname,
            ParserName = parserName,
            RawPreserved = true
        };

        return new SourceEvent(metadata, fields);
    }

    private static string? TryGet(IReadOnlyDictionary<string, object?> fields, string key)
        => fields.TryGetValue(key, out var value) ? value?.ToString() : null;
}