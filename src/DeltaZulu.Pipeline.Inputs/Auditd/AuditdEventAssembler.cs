using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Inputs.Auditd;

public sealed class AuditdEventAssembler
{
    private static readonly HashSet<string> CompletionRecordTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "EOE", "PROCTITLE"
    };

    private static readonly HashSet<string> MultiInstanceRecordTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH", "SOCKADDR", "OBJ_PID", "FD_PAIR", "BPRM_FCAPS"
    };

    private readonly Dictionary<string, List<AuditdRecord>> _pending = new(StringComparer.OrdinalIgnoreCase);

    public SourceEvent? Accept(AuditdRecord record, bool flushImmediately = false)
    {
        if (record.Type.Equals("EOE", StringComparison.OrdinalIgnoreCase))
        {
            return Flush(record.Id);
        }

        if (!_pending.TryGetValue(record.Id, out var records))
        {
            records = [];
            _pending[record.Id] = records;
        }

        records.Add(record);

        if (flushImmediately || CompletionRecordTypes.Contains(record.Type))
        {
            return Flush(record.Id);
        }

        return null;
    }

    public SourceEvent? Flush(string id)
    {
        if (!_pending.Remove(id, out var records) || records.Count == 0)
        {
            return null;
        }

        return BuildEvent(id, records);
    }

    public IEnumerable<SourceEvent> FlushAll()
    {
        foreach (var id in _pending.Keys.ToList())
        {
            var evt = Flush(id);
            if (evt is not null)
            {
                yield return evt;
            }
        }
    }

    public int PendingCount => _pending.Count;

    private static SourceEvent BuildEvent(string id, IReadOnlyList<AuditdRecord> records)
    {
        var eventFields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
            ["ID"] = id,
            ["RawEvent"] = records.Select(r => r.RawLine).ToArray()
        };

        foreach (var group in records.GroupBy(r => r.Type, StringComparer.OrdinalIgnoreCase))
        {
            var values = group.Select(NormalizeRecord).ToList();
            if (MultiInstanceRecordTypes.Contains(group.Key))
            {
                eventFields[group.Key] = values;
            }
            else if (values.Count == 1)
            {
                eventFields[group.Key] = values[0];
            }
            else
            {
                eventFields[group.Key] = values;
            }
        }

        var metadata = new ResourceMetadata {
            SourceType = "LinuxAuditd",
            SourceName = "auditd",
            Platform = "linux",
            Hostname = Environment.MachineName,
            ParserName = nameof(AuditdEventAssembler),
            RawPreserved = true
        };

        return new SourceEvent(metadata, eventFields);
    }

    private static Dictionary<string, object?> NormalizeRecord(AuditdRecord record)
    {
        var fields = new Dictionary<string, object?>(record.Fields, StringComparer.OrdinalIgnoreCase);

        if (record.Type.Equals("EXECVE", StringComparison.OrdinalIgnoreCase))
        {
            var args = fields
                .Where(k => k.Key.StartsWith("a", StringComparison.OrdinalIgnoreCase) && int.TryParse(k.Key[1..], out _))
                .OrderBy(k => int.Parse(k.Key[1..]))
                .Select(k => k.Value)
                .ToArray();
            fields["ARGV"] = args;
        }

        if (record.Type.Equals("PROCTITLE", StringComparison.OrdinalIgnoreCase) && fields.TryGetValue("proctitle", out var proctitle) && proctitle is string s)
        {
            fields["ARGV"] = s.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }

        if (record.Type.Equals("PATH", StringComparison.OrdinalIgnoreCase) && fields.TryGetValue("name", out var nameVal) && nameVal is string name)
        {
            var decoded = TryDecodeHexPath(name);
            if (decoded is not null)
            {
                fields["name"] = decoded;
            }
        }

        return fields;
    }

    private static string? TryDecodeHexPath(string value)
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
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }
}