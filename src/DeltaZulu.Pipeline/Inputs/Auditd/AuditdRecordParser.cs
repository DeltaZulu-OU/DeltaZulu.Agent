using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DeltaZulu.Pipeline.Inputs.Common;

namespace DeltaZulu.Pipeline.Inputs.Auditd;

public sealed partial class AuditdRecordParser
{
    private static readonly Regex PrefixRegex = CreatePrefixRegex();

    public AuditdRecord Parse(string line)
    {
        var prefix = PrefixRegex.Match(line);
        if (!prefix.Success)
        {
            throw new FormatException("Line does not look like a Linux audit record.");
        }

        var type = prefix.Groups["type"].Value;
        var id = prefix.Groups["id"].Value;
        var payload = prefix.Groups["payload"].Value;
        var fields = ParseKeyValuePayload(payload);

        return new AuditdRecord(id, type, fields, line);
    }

    private static object? CoerceValue(string key, string value, bool quoted)
    {
        if (value == "(null)")
        {
            return null;
        }

        if ((key.StartsWith("a", StringComparison.OrdinalIgnoreCase) && key.Length > 1 && int.TryParse(key[1..], out _))
            || key.Equals("proctitle", StringComparison.OrdinalIgnoreCase))
        {
            var decoded = TryDecodeHexString(value);
            if (decoded is not null)
            {
                return decoded;
            }
        }

        if (!quoted)
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            {
                return l;
            }

            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return value;
    }

    [GeneratedRegex(@"^type=(?<type>[A-Z0-9_]+)\s+msg=audit\((?<id>[^)]+)\):\s*(?<payload>.*)$")]
    private static partial Regex CreatePrefixRegex();

    private static Dictionary<string, object?> ParseKeyValuePayload(string payload)
                => LogFieldNormalizer.ParseKeyValueFields(payload, CoerceValue);

    private static string? TryDecodeHexString(string value)
    {
        if (value.Length == 0 || value.Length % 2 != 0)
        {
            return null;
        }

        for (var i = 0; i < value.Length; i++)
        {
            if (!Uri.IsHexDigit(value[i]))
            {
                return null;
            }
        }

        try
        {
            var bytes = Convert.FromHexString(value);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }
}
