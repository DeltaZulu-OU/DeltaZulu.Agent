using DeltaZulu.Agent.Shared.Pipeline.Events;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DeltaZulu.Agent.Inputs.Syslog;

/// <summary>
/// Dependency-free syslog parser used to avoid taking a Microsoft.Syslog project dependency.
/// It covers common RFC 3164 and RFC 5424 shapes and preserves RawMessage for server-side recovery.
/// A stricter parser package can replace this class behind the same input boundary later.
/// </summary>
public sealed partial class LightweightSyslogParser
{
    private static readonly Regex PriorityRegex = CreatePriorityRegex();
    private static readonly Regex Rfc5424Regex = CreateRfc5424Regex();
    private static readonly Regex Rfc3164Regex = CreateRfc3164Regex();
    private static readonly Regex KeyValueRegex = CreateKeyValueRegex();

    public SourceEvent Parse(string rawMessage, string sourceName, string? sourceAddress = null)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["RawMessage"] = rawMessage,
            ["ReceivedAt"] = receivedAt
        };

        if (!string.IsNullOrWhiteSpace(sourceAddress))
        {
            fields["SourceIpAddress"] = sourceAddress;
        }

        var body = rawMessage;
        var priorityMatch = PriorityRegex.Match(body);
        if (priorityMatch.Success && int.TryParse(priorityMatch.Groups["pri"].Value, out var pri))
        {
            var decoded = SyslogPriority.Decode(pri);
            fields["Priority"] = pri;
            fields["Facility"] = decoded.Facility;
            fields["Severity"] = decoded.Severity;
            body = priorityMatch.Groups["rest"].Value.TrimStart();
        }

        if (TryParseRfc5424(body, fields) || TryParseRfc3164(body, fields))
        {
            ExtractKeyValues(fields);
            return CreateEvent(fields, sourceName);
        }

        fields["Message"] = body;
        ExtractKeyValues(fields);
        return CreateEvent(fields, sourceName);
    }

    private static bool TryParseRfc5424(string body, IDictionary<string, object?> fields)
    {
        var match = Rfc5424Regex.Match(body);
        if (!match.Success)
        {
            return false;
        }

        fields["SyslogVersion"] = match.Groups["version"].Value;
        if (DateTimeOffset.TryParse(match.Groups["timestamp"].Value, out var timestamp))
        {
            fields["Timestamp"] = timestamp;
        }
        else
        {
            fields["Timestamp"] = match.Groups["timestamp"].Value;
        }

        fields["Hostname"] = NullDash(match.Groups["host"].Value);
        fields["AppName"] = NullDash(match.Groups["app"].Value);
        fields["ProcessName"] = NullDash(match.Groups["app"].Value);
        fields["ProcId"] = NullDash(match.Groups["proc"].Value);
        if (int.TryParse(NullDash(match.Groups["proc"].Value), out var pid))
        {
            fields["ProcessId"] = pid;
        }

        fields["MsgId"] = NullDash(match.Groups["msgid"].Value);
        fields["StructuredData"] = NullDash(match.Groups["structured"].Value);
        fields["Message"] = match.Groups["message"].Value;
        return true;
    }

    private static bool TryParseRfc3164(string body, IDictionary<string, object?> fields)
    {
        var match = Rfc3164Regex.Match(body);
        if (!match.Success)
        {
            return false;
        }

        var timestampText = match.Groups["timestamp"].Value;
        if (DateTime.TryParseExact(timestampText, "MMM d HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var timestamp)
            || DateTime.TryParseExact(timestampText, "MMM dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out timestamp))
        {
            var now = DateTimeOffset.Now;
            fields["Timestamp"] = new DateTimeOffset(now.Year, timestamp.Month, timestamp.Day, timestamp.Hour, timestamp.Minute, timestamp.Second, now.Offset);
        }
        else
        {
            fields["Timestamp"] = timestampText;
        }

        fields["Hostname"] = match.Groups["host"].Value;
        fields["ProcessName"] = match.Groups["proc"].Value;
        fields["AppName"] = match.Groups["proc"].Value;
        if (int.TryParse(match.Groups["pid"].Value, out var pid))
        {
            fields["ProcessId"] = pid;
        }

        fields["Message"] = match.Groups["message"].Value;
        return true;
    }

    private static void ExtractKeyValues(IDictionary<string, object?> fields)
    {
        if (!fields.TryGetValue("Message", out var messageObject) || messageObject is not string message)
        {
            return;
        }

        var extracted = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in KeyValueRegex.Matches(message))
        {
            var key = match.Groups["key"].Value;
            var value = match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value.Trim('"')
                : match.Groups["value"].Value;
            extracted[key] = value;
        }

        if (extracted.Count > 0)
        {
            fields["ExtractedData"] = extracted;
        }
    }

    private static string? NullDash(string value) => value == "-" ? null : value;

    private static SourceEvent CreateEvent(IReadOnlyDictionary<string, object?> fields, string sourceName)
    {
        var hostname = fields.TryGetValue("Hostname", out var host) ? host?.ToString() : Environment.MachineName;
        var metadata = new ResourceMetadata
        {
            SourceType = "LinuxSyslog",
            SourceName = sourceName,
            Platform = "linux",
            Hostname = string.IsNullOrWhiteSpace(hostname) ? Environment.MachineName : hostname!,
            ParserName = nameof(LightweightSyslogParser),
            RawPreserved = true
        };

        return new SourceEvent(metadata, fields);
    }

    [GeneratedRegex(@"^<(?<pri>\d{1,3})>(?<rest>.*)$")]
    private static partial Regex CreatePriorityRegex();

    [GeneratedRegex(@"^(?<version>\d)\s+(?<timestamp>\S+)\s+(?<host>\S+)\s+(?<app>\S+)\s+(?<proc>\S+)\s+(?<msgid>\S+)\s+(?<structured>(?:-|\[.*\]))\s*(?<message>.*)$")]
    private static partial Regex CreateRfc5424Regex();

    [GeneratedRegex(@"^(?<timestamp>[A-Z][a-z]{2}\s+\d{1,2}\s+\d{2}:\d{2}:\d{2})\s+(?<host>\S+)\s+(?<proc>[^\s:\[]+)(?:\[(?<pid>\d+)\])?:?\s*(?<message>.*)$")]
    private static partial Regex CreateRfc3164Regex();

    [GeneratedRegex(@"(?<key>[A-Za-z][A-Za-z0-9_\.-]{1,64})=(?:(?<quoted>""[^""]*"")|(?<value>[^\s]+))")]
    private static partial Regex CreateKeyValueRegex();
}
