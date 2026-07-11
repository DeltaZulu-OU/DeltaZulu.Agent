using DeltaZulu.Agent.SchemaMetadata;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Agent.ProfileWorkbench;

public sealed record WorkbenchSchemaTreeNode(
    string Text,
    WorkbenchSchemaTreeNodeKind Kind,
    string? Table = null,
    string? Source = null,
    string? Field = null,
    string? ProfileId = null,
    IReadOnlyList<WorkbenchSchemaTreeNode>? Children = null)
{
    public IReadOnlyList<WorkbenchSchemaTreeNode> Children { get; init; } = Children ?? [];
}

public enum WorkbenchSchemaTreeNodeKind
{
    Root,
    LogSource,
    Field
}

public static class WorkbenchSchemaTree
{
    public const string VirtualRootName = "none";

    public static WorkbenchSchemaTreeNode Build(IEnumerable<ResourceProfile> profiles)
    {
        var sources = profiles
            .Where(profile => profile.Enabled)
            .Select(TryCreateSource)
            .Where(source => source is not null)
            .Select(source => source!)
            .GroupBy(source => $"{source.Table}\u001f{source.Source}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.First().Table, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.First().Source, StringComparer.OrdinalIgnoreCase)
            .Select(group => {
                var fields = group
                    .SelectMany(source => source.Fields)
                    .GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(field => new WorkbenchSchemaTreeNode(
                        field.Key,
                        WorkbenchSchemaTreeNodeKind.Field,
                        group.First().Table,
                        group.First().Source,
                        field.Key,
                        group.First().ProfileId))
                    .ToArray();

                return new WorkbenchSchemaTreeNode(
                    $"{group.First().Table}/{group.First().Source}",
                    WorkbenchSchemaTreeNodeKind.LogSource,
                    group.First().Table,
                    group.First().Source,
                    ProfileId: group.First().ProfileId,
                    Children: fields);
            })
            .ToArray();

        return new WorkbenchSchemaTreeNode(VirtualRootName, WorkbenchSchemaTreeNodeKind.Root, Children: sources);
    }

    public static string? InsertionText(WorkbenchSchemaTreeNode node) => node.Kind switch
    {
        WorkbenchSchemaTreeNodeKind.LogSource when !string.IsNullOrWhiteSpace(node.Table) && !string.IsNullOrWhiteSpace(node.Source)
            => $"{node.Table} | Source ~= \"{node.Source}\" ",
        WorkbenchSchemaTreeNodeKind.Field when !string.IsNullOrWhiteSpace(node.Field)
            => EscapeFieldName(node.Field),
        _ => null
    };

    private static SourceSchema? TryCreateSource(ResourceProfile profile)
    {
        var table = NormalizeTable(profile.Resource.Family, profile.Input.Table);
        var source = PrimarySource(profile);
        if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var schema = SchemaTextParser.Parse(table, profile.Input.Schema, NormalizeFamily(profile.Resource.Family), executable: false);
        return new SourceSchema(table, source, profile.Id, schema.Fields);
    }

    private static string NormalizeTable(string? family, string? table)
    {
        if (!string.IsNullOrWhiteSpace(table))
        {
            return table.Trim() switch
            {
                "Etw" => "ETW",
                "EventLog" => "Eventlog",
                var value => value
            };
        }

        return NormalizeFamily(family) switch
        {
            "etw" => "ETW",
            "eventlog" => "Eventlog",
            "auditd" => "Auditd",
            "syslog" => "Syslog",
            var value when !string.IsNullOrWhiteSpace(value) => char.ToUpperInvariant(value[0]) + value[1..],
            _ => "Source"
        };
    }

    private static string? PrimarySource(ResourceProfile profile)
    {
        var family = NormalizeFamily(profile.Resource.Family);
        return family switch
        {
            "eventlog" => TrimOperationalSuffix(profile.Resource.Channel),
            "etw" => TrimOperationalSuffix(profile.Resource.Provider ?? profile.Resource.Session ?? profile.Resource.Channel),
            "auditd" => profile.Resource.RecordTypes.FirstOrDefault(type => !string.IsNullOrWhiteSpace(type)) ?? FirstConfiguredOption(profile, "type", "recordType", "syscallType") ?? profile.Resource.Channel ?? profile.Resource.Family,
            "syslog" => FirstNonWhiteSpace(
                profile.Resource.Service,
                FirstConfiguredOption(profile, "application", "appName", "processName"),
                profile.Resource.Channel,
                profile.Resource.Family),
            _ => FirstNonWhiteSpace(profile.Resource.Service, profile.Resource.Channel, profile.Resource.Provider, profile.Resource.Session, profile.Resource.Family)
        };
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? TrimOperationalSuffix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        const string operational = "/Operational";
        return trimmed.EndsWith(operational, StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^operational.Length]
            : trimmed;
    }

    private static string? FirstConfiguredOption(ResourceProfile profile, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (profile.Resource.Options.TryGetValue(key, out var value) && value is not null && !string.IsNullOrWhiteSpace(value.ToString()))
            {
                return value.ToString()!.Trim();
            }
        }

        return null;
    }

    private static string NormalizeFamily(string? family) => family?.Trim().ToLowerInvariant() switch
    {
        "windows.eventlog" => "eventlog",
        "line" => "lines",
        { } value => value,
        _ => string.Empty
    };

    private static string EscapeFieldName(string field) => field.All(c => char.IsLetterOrDigit(c) || c == '_') ? field : $"['{field.Replace("'", "\\'")}']";

    private sealed record SourceSchema(string Table, string Source, string ProfileId, IReadOnlyList<SchemaFieldDescriptor> Fields);
}
