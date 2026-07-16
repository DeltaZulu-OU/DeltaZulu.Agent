namespace DeltaZulu.Agent.SchemaMetadata;

public static class SchemaTextParser
{
    public static SchemaDescriptor Parse(
        string table,
        string schemaText,
        string sourceKind,
        string provenance = "ProfileContract",
        int confidence = 90,
        bool executable = false)
    {
        if (TryParseKnownSchema(table, schemaText, sourceKind, executable, out var knownSchema))
        {
            return knownSchema;
        }

        var fields = new List<SchemaFieldDescriptor>();
        if (!string.IsNullOrWhiteSpace(schemaText))
        {
            foreach (var item in schemaText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = item.IndexOf(':');
                var name = separator > 0 ? item[..separator].Trim() : item.Trim();
                var type = separator > 0 && separator + 1 < item.Length ? item[(separator + 1)..].Trim() : "dynamic";
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                fields.Add(new SchemaFieldDescriptor(
                    name,
                    string.IsNullOrWhiteSpace(type) ? "dynamic" : NormalizeType(type),
                    provenance,
                    confidence));
            }
        }

        return CreateDescriptor(table, fields, sourceKind, provenance, confidence, executable);
    }

    public static SchemaDescriptor Lines(string table = "Lines") => CreateDescriptor(
        table,
        [
            new SchemaFieldDescriptor("lineNumber", "long", "ParserContract", 100, Nullable: false),
            new SchemaFieldDescriptor("line", "string", "ParserContract", 100, Nullable: false)
        ],
        "lines",
        "ParserContract",
        100,
        true);

    public static SchemaDescriptor Empty(string table, string sourceKind, string provenance = "Unavailable") =>
        new(table, [], sourceKind, provenance, 0, Executable: false);

    private static bool TryParseKnownSchema(
        string table,
        string schemaText,
        string sourceKind,
        bool executable,
        out SchemaDescriptor descriptor)
    {
        var schema = schemaText?.Trim() ?? string.Empty;
        var fields = schema switch
        {
            "WindowsEventLog.Native" => CreateFields([
                ("ProviderName", "string"),
                ("EventId", "int"),
                ("Qualifiers", "int"),
                ("Channel", "string"),
                ("RecordId", "long"),
                ("Level", "string"),
                ("Opcode", "string"),
                ("Task", "string"),
                ("Keywords", "string"),
                ("MachineName", "string"),
                ("ProcessId", "int"),
                ("ThreadId", "int"),
                ("TimeCreated", "datetime"),
                ("EventData", "dynamic"),
                ("RawEvent", "string"),
                ("Message", "string")
            ], schema),
            "WindowsEtw.Native" => CreateFields([
                ("ProviderGuid", "guid"),
                ("ProviderName", "string"),
                ("EventId", "int"),
                ("EventName", "string"),
                ("Opcode", "int"),
                ("OpcodeName", "string"),
                ("Version", "int"),
                ("Task", "int"),
                ("TaskName", "string"),
                ("LevelCode", "int"),
                ("Level", "string"),
                ("Keywords", "long"),
                ("TimeStamp", "datetime"),
                ("TimestampRaw", "long"),
                ("ProcessId", "int"),
                ("ThreadId", "int"),
                ("ActivityId", "guid"),
                ("RelatedActivityId", "guid"),
                ("Channel", "int"),
                ("ProcessorId", "int"),
                ("PayloadLength", "int")
            ], schema),
            "LinuxSyslog.Native" => CreateFields([
                ("RawMessage", "string"),
                ("ReceivedAt", "datetime"),
                ("SourceIpAddress", "string"),
                ("Priority", "int"),
                ("Facility", "string"),
                ("Severity", "string"),
                ("SyslogVersion", "string"),
                ("Timestamp", "datetime"),
                ("Hostname", "string"),
                ("AppName", "string"),
                ("ProcessName", "string"),
                ("ProcId", "string"),
                ("ProcessId", "int"),
                ("MsgId", "string"),
                ("StructuredData", "string"),
                ("Message", "string"),
                ("ExtractedData", "dynamic")
            ], schema),
            "LinuxAuditd.AssembledEvent" => CreateFields([
                ("ID", "string"),
                ("RawEvent", "dynamic"),
                ("SYSCALL", "dynamic"),
                ("EXECVE", "dynamic"),
                ("PROCTITLE", "dynamic"),
                ("PATH", "dynamic"),
                ("CWD", "dynamic"),
                ("SOCKADDR", "dynamic"),
                ("OBJ_PID", "dynamic"),
                ("FD_PAIR", "dynamic"),
                ("BPRM_FCAPS", "dynamic"),
                ("ARGV", "dynamic")
            ], schema),
            _ => null
        };

        descriptor = fields is null
            ? Empty(table, sourceKind)
            : CreateDescriptor(table, fields, sourceKind, schema, 100, executable);
        return fields is not null;
    }

    private static SchemaDescriptor CreateDescriptor(
        string table,
        IReadOnlyList<SchemaFieldDescriptor> fields,
        string sourceKind,
        string provenance,
        int confidence,
        bool executable)
    {
        var queryableFields = fields.ToList();
        AddRuntimeField(queryableFields, "source", "string", "RuntimeEnvelope");
        AddRuntimeField(queryableFields, "_metadata", "dynamic", "RuntimeEnvelope");
        return new SchemaDescriptor(table, queryableFields, sourceKind, provenance, confidence, executable);
    }

    private static void AddRuntimeField(List<SchemaFieldDescriptor> fields, string name, string kqlType, string origin)
    {
        if (fields.Any(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        fields.Add(new SchemaFieldDescriptor(name, kqlType, origin, 100));
    }

    private static IReadOnlyList<SchemaFieldDescriptor> CreateFields(
        IEnumerable<(string Name, string Type)> fields,
        string origin) =>
        fields
            .Select(field => new SchemaFieldDescriptor(field.Name, field.Type, origin, 100))
            .ToArray();

    private static string NormalizeType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "int32" or "integer" => "int",
        "int64" => "long",
        "datetimeoffset" => "datetime",
        "boolean" => "bool",
        _ => value.Trim().ToLowerInvariant()
    };
}
