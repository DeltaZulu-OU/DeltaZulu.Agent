using DeltaZulu.Agent.Core.Events;

namespace DeltaZulu.Agent.Inputs.Auditd;

/// <summary>
/// LAUREL-inspired audit event assembler. It groups audit records by msg=audit(TIME:SEQUENCE).
/// This restructuring keeps it intentionally conservative; process tracking/enrichment is roadmap-only.
/// </summary>
public sealed class AuditdEventAssembler
{
    private readonly Dictionary<string, List<AuditdRecord>> _pending = new(StringComparer.OrdinalIgnoreCase);

    public SourceEvent? Accept(AuditdRecord record, bool flushImmediately = false)
    {
        if (!_pending.TryGetValue(record.Id, out var records))
        {
            records = [];
            _pending[record.Id] = records;
        }

        records.Add(record);
        if (flushImmediately)
        {
            return Flush(record.Id);
        }

        return null;
    }

    public SourceEvent? Flush(string id)
    {
        if (!_pending.Remove(id, out var records)) return null;
        return BuildEvent(id, records);
    }

    public IEnumerable<SourceEvent> FlushAll()
    {
        foreach (var id in _pending.Keys.ToList())
        {
            var evt = Flush(id);
            if (evt is not null) yield return evt;
        }
    }

    private static SourceEvent BuildEvent(string id, IReadOnlyList<AuditdRecord> records)
    {
        var eventFields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ID"] = id,
            ["RawEvent"] = records.Select(r => r.RawLine).ToArray()
        };

        foreach (var group in records.GroupBy(r => r.Type, StringComparer.OrdinalIgnoreCase))
        {
            var values = group.Select(r => NormalizeRecord(r)).ToList();
            if (group.Key is "PATH" or "SOCKADDR")
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

        var metadata = new ResourceMetadata
        {
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

        return fields;
    }
}