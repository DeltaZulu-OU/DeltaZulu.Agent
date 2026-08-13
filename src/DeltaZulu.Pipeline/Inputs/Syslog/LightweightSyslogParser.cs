using DeltaZulu.Parse;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Inputs.Common;

namespace DeltaZulu.Pipeline.Inputs.Syslog;

/// <summary>
/// Maps the RFC 3164 and RFC 5424 envelopes decoded by DeltaZulu.Parse into
/// the agent's source-event contract while preserving RawMessage for recovery.
/// </summary>
public sealed class LightweightSyslogParser
{
    public SourceEvent Parse(string rawMessage, string sourceName, string? sourceAddress = null)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
            ["RawMessage"] = rawMessage,
            ["ReceivedAt"] = receivedAt
        };

        if (!string.IsNullOrWhiteSpace(sourceAddress))
        {
            fields["SourceIpAddress"] = sourceAddress;
        }

        if (SyslogDecoder.TryDecode(rawMessage, out var envelope))
        {
            ApplyEnvelope(envelope, fields);
            ExtractKeyValues(fields);
            return CreateEvent(fields, sourceName);
        }

        fields["Message"] = rawMessage;
        ExtractKeyValues(fields);
        return CreateEvent(fields, sourceName);
    }

    private static SourceEvent CreateEvent(IReadOnlyDictionary<string, object?> fields, string sourceName)
    {
        var hostname = fields.TryGetValue("Hostname", out var host) ? host?.ToString() : Environment.MachineName;
        var metadata = new ResourceMetadata {
            SourceType = "LinuxSyslog",
            SourceName = sourceName,
            Platform = "linux",
            Hostname = string.IsNullOrWhiteSpace(hostname) ? Environment.MachineName : hostname!,
            ParserName = nameof(LightweightSyslogParser),
            RawPreserved = true
        };

        return new SourceEvent(metadata, fields);
    }

    private static void ApplyEnvelope(SyslogEnvelope envelope, IDictionary<string, object?> fields)
    {
        if (envelope.Facility is int facility && envelope.Severity is int severity)
        {
            var priority = (facility * 8) + severity;
            var decoded = SyslogPriority.Decode(priority);
            fields["Priority"] = priority;
            fields["Facility"] = decoded.Facility;
            fields["Severity"] = decoded.Severity;
        }

        fields["Message"] = envelope.Msg;
        fields["Timestamp"] = envelope.Timestamp;
        fields["Hostname"] = envelope.Host;
        fields["AppName"] = envelope.AppName;
        fields["ProcessName"] = envelope.AppName;

        if (envelope.Framing == SyslogFraming.Rfc5424)
        {
            fields["SyslogVersion"] = "1";
            fields["MsgId"] = envelope.MsgId;
            fields["StructuredData"] = envelope.StructuredData;
            fields["ProcId"] = envelope.ProcId;
        }

        if (int.TryParse(envelope.ProcId, out var processId))
        {
            fields["ProcessId"] = processId;
        }
    }

    private static void ExtractKeyValues(IDictionary<string, object?> fields)
    {
        if (!fields.TryGetValue("Message", out var messageObject) || messageObject is not string message)
        {
            return;
        }

        var extracted = LogFieldNormalizer.ParseKeyValueFields(message, static (_, value, _) => value);

        if (extracted.Count > 0)
        {
            fields["ExtractedData"] = extracted;
        }
    }
}
