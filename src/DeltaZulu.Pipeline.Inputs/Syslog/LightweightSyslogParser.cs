using System.Globalization;
using System.Text.RegularExpressions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Inputs.Common;

namespace DeltaZulu.Pipeline.Inputs.Syslog;

/// <summary>
/// Dependency-free syslog parser used to avoid taking a Microsoft.Syslog project dependency.
/// It covers common RFC 3164 and RFC 5424 shapes and preserves RawMessage for server-side recovery.
/// A stricter parser package can replace this class behind the same input boundary later.
/// </summary>
public sealed partial class LightweightSyslogParser
{
    private static readonly Regex PriorityRegex = CreatePriorityRegex();
    private static readonly Regex Rfc3164Regex = CreateRfc3164Regex();

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
        var cursor = 0;
        if (!ReadToken(body, ref cursor, out var version)
            || !version.All(char.IsDigit)
            || !ReadToken(body, ref cursor, out var timestampText)
            || !ReadToken(body, ref cursor, out var host)
            || !ReadToken(body, ref cursor, out var app)
            || !ReadToken(body, ref cursor, out var proc)
            || !ReadToken(body, ref cursor, out var msgid)
            || !ReadStructuredData(body, ref cursor, out var structuredData))
        {
            return false;
        }

        fields["SyslogVersion"] = version;
        if (DateTimeOffset.TryParse(
            timestampText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var timestamp))
        {
            fields["Timestamp"] = timestamp;
        }
        else
        {
            fields["Timestamp"] = timestampText;
        }

        fields["Hostname"] = NullDash(host);
        fields["AppName"] = NullDash(app);
        fields["ProcessName"] = NullDash(app);
        fields["ProcId"] = NullDash(proc);
        if (int.TryParse(NullDash(proc), NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
        {
            fields["ProcessId"] = pid;
        }

        fields["MsgId"] = NullDash(msgid);
        fields["StructuredData"] = NullDash(structuredData);
        fields["Message"] = cursor < body.Length ? body[cursor..] : string.Empty;
        return true;
    }

    private static bool ReadToken(string text, ref int cursor, out string token)
    {
        while (cursor < text.Length && text[cursor] == ' ')
        {
            cursor++;
        }

        var start = cursor;
        while (cursor < text.Length && text[cursor] != ' ')
        {
            cursor++;
        }

        token = text[start..cursor];
        return token.Length > 0;
    }

    private static bool ReadStructuredData(string text, ref int cursor, out string structuredData)
    {
        while (cursor < text.Length && text[cursor] == ' ')
        {
            cursor++;
        }

        structuredData = string.Empty;
        if (cursor >= text.Length)
        {
            return false;
        }

        if (text[cursor] == '-')
        {
            structuredData = "-";
            cursor++;
            if (cursor < text.Length && text[cursor] == ' ')
            {
                cursor++;
            }

            return true;
        }

        if (text[cursor] != '[')
        {
            return false;
        }

        var start = cursor;
        var inQuote = false;
        var escaped = false;
        while (cursor < text.Length)
        {
            var ch = text[cursor++];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\' && inQuote)
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (ch == ']' && !inQuote)
            {
                if (cursor >= text.Length || text[cursor] != '[')
                {
                    structuredData = text[start..cursor];
                    if (cursor < text.Length && text[cursor] == ' ')
                    {
                        cursor++;
                    }

                    return true;
                }
            }
        }

        return false;
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
        if (int.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
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

        var extracted = LogFieldNormalizer.ParseKeyValueFields(message, static (_, value, _) => value);

        if (extracted.Count > 0)
        {
            fields["ExtractedData"] = extracted;
        }
    }

    private static string? NullDash(string value) => value == "-" ? null : value;

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

    [GeneratedRegex(@"^<(?<pri>\d{1,3})>(?<rest>.*)$")]
    private static partial Regex CreatePriorityRegex();

    [GeneratedRegex(@"^(?<timestamp>[A-Z][a-z]{2}\s+\d{1,2}\s+\d{2}:\d{2}:\d{2})\s+(?<host>\S+)\s+(?<proc>[^\s:\[]+)(?:\[(?<pid>\d+)\])?:?\s*(?<message>.*)$")]
    private static partial Regex CreateRfc3164Regex();

}
