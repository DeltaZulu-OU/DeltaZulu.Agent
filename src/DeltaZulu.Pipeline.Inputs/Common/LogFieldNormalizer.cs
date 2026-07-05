using System.Globalization;
using System.Text;

namespace DeltaZulu.Pipeline.Inputs.Common;

/// <summary>
/// Shared line-oriented helpers for source-specific inputs that need to normalize
/// parsed fields into dictionaries while preserving source-native field names.
/// This intentionally stays format-neutral: syslog, auditd, W3C/IIS-style rows,
/// DNS debug rows, and future text inputs can layer their own record framing on
/// top of these key/value and delimited-field primitives.
/// </summary>
public static class LogFieldNormalizer
{
    public static Dictionary<string, object?> ParseKeyValueFields(
        string text,
        Func<string, string, bool, object?>? coerceValue = null)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var cursor = 0;
        while (cursor < text.Length)
        {
            SkipSeparators(text, ref cursor);
            var keyStart = cursor;
            while (cursor < text.Length && text[cursor] != '=' && !IsSeparator(text[cursor]))
            {
                cursor++;
            }

            if (keyStart == cursor || cursor >= text.Length || text[cursor] != '=')
            {
                SkipToken(text, ref cursor);
                continue;
            }

            var key = text[keyStart..cursor];
            cursor++;
            var value = ReadValue(text, ref cursor, out var quoted);
            result[key] = coerceValue is null
                ? CoerceScalar(value, quoted)
                : coerceValue(key, value, quoted);
        }

        return result;
    }

    public static Dictionary<string, object?> ParseDelimitedFields(
        IReadOnlyList<string> fieldNames,
        string line,
        char delimiter = ' ',
        bool dashIsNull = true)
    {
        var values = SplitDelimited(line, delimiter);
        var result = new Dictionary<string, object?>(fieldNames.Count, StringComparer.OrdinalIgnoreCase);
        var count = Math.Min(fieldNames.Count, values.Count);
        for (var i = 0; i < count; i++)
        {
            var value = values[i];
            result[fieldNames[i]] = dashIsNull && value == "-"
                ? null
                : CoerceScalar(value, quoted: false);
        }

        if (values.Count > fieldNames.Count)
        {
            result["_extra"] = values.Skip(fieldNames.Count).ToArray();
        }

        return result;
    }

    public static object? CoerceScalar(string value, bool quoted)
    {
        if (value.Equals("(null)", StringComparison.OrdinalIgnoreCase)
            || value.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!quoted)
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            {
                return l;
            }

            if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                return d;
            }

            if (bool.TryParse(value, out var b))
            {
                return b;
            }
        }

        return value;
    }

    private static string ReadValue(string text, ref int cursor, out bool quoted)
    {
        quoted = cursor < text.Length && text[cursor] == '"';
        if (!quoted)
        {
            var valueStart = cursor;
            while (cursor < text.Length && !IsSeparator(text[cursor]))
            {
                cursor++;
            }

            return text[valueStart..cursor];
        }

        cursor++;
        var sb = new StringBuilder();
        var escaped = false;
        while (cursor < text.Length)
        {
            var ch = text[cursor++];
            if (escaped)
            {
                sb.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                break;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static List<string> SplitDelimited(string line, char delimiter)
    {
        var values = new List<string>();
        var cursor = 0;
        while (cursor < line.Length)
        {
            if (delimiter == ' ')
            {
                while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
                {
                    cursor++;
                }
            }

            if (cursor >= line.Length)
            {
                break;
            }

            values.Add(ReadDelimitedValue(line, ref cursor, delimiter));
            if (cursor < line.Length && line[cursor] == delimiter)
            {
                cursor++;
            }
        }

        return values;
    }

    private static string ReadDelimitedValue(string line, ref int cursor, char delimiter)
    {
        if (line[cursor] != '"')
        {
            var start = cursor;
            while (cursor < line.Length && (delimiter == ' ' ? !char.IsWhiteSpace(line[cursor]) : line[cursor] != delimiter))
            {
                cursor++;
            }

            return line[start..cursor];
        }

        cursor++;
        var sb = new StringBuilder();
        var escaped = false;
        while (cursor < line.Length)
        {
            var ch = line[cursor++];
            if (escaped)
            {
                sb.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                break;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static void SkipSeparators(string text, ref int cursor)
    {
        while (cursor < text.Length && IsSeparator(text[cursor]))
        {
            cursor++;
        }
    }

    private static void SkipToken(string text, ref int cursor)
    {
        while (cursor < text.Length && !IsSeparator(text[cursor]))
        {
            cursor++;
        }
    }

    private static bool IsSeparator(char ch) => char.IsWhiteSpace(ch) || ch == ',' || ch == ';';
}
