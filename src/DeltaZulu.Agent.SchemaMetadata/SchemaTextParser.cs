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

        return new SchemaDescriptor(table, fields, sourceKind, provenance, confidence, executable);
    }

    public static SchemaDescriptor Lines(string table = "Lines") => new(
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

    private static string NormalizeType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "int32" or "integer" => "int",
        "int64" => "long",
        "datetimeoffset" => "datetime",
        "boolean" => "bool",
        _ => value.Trim().ToLowerInvariant()
    };
}
