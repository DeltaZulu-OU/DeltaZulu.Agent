using System.Text;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Pipeline.Core.Planning;

public sealed class ExecutionPlanCompiler
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    private static readonly string[] PlanningOptionKeys = [
        "framing",
        "payloadFormat",
        "admission",
        "parserDomain"
    ];

    public ExecutionPlan Compile(IEnumerable<ResourceProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var groups = profiles
            .Where(profile => profile.Enabled)
            .Select(CreateCandidate)
            .GroupBy(candidate => candidate.AcquisitionKey, StringComparer.Ordinal)
            .Select(CreatePlan)
            .OrderBy(plan => plan.AcquisitionKey, StringComparer.Ordinal)
            .ToArray();

        return new ExecutionPlan(groups);
    }

    private static Candidate CreateCandidate(ResourceProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            throw new ExecutionPlanCompilationException("enabled profiles must have an id");
        }

        var resource = profile.Resource;
        var kind = NormalizeRequired(resource.Family, $"profile '{profile.Id}' resource.family");
        var framing = ReadOption(resource, "framing") ?? InferFraming(kind, resource.Mode);
        var payloadFormat = ReadOption(resource, "payloadFormat") ?? InferPayloadFormat(profile.Input.Schema, kind);
        var admission = ReadOption(resource, "admission") ?? InferAdmission(kind, payloadFormat);
        var parserDomain = ReadOption(resource, "parserDomain") ?? InferParserDomain(kind, payloadFormat);
        var acquisitionKey = BuildAcquisitionKey(resource, kind);

        return new Candidate(
            profile.Id,
            acquisitionKey,
            kind,
            NormalizeToken(framing),
            NormalizeToken(payloadFormat),
            NormalizeToken(admission),
            NormalizeToken(parserDomain));
    }

    private static ResourceAcquisitionPlan CreatePlan(IGrouping<string, Candidate> group)
    {
        var candidates = group.OrderBy(candidate => candidate.ProfileId, StringComparer.Ordinal).ToArray();
        var first = candidates[0];
        var conflict = candidates.FirstOrDefault(candidate =>
            !Comparer.Equals(candidate.Kind, first.Kind)
            || !Comparer.Equals(candidate.Framing, first.Framing)
            || !Comparer.Equals(candidate.PayloadFormat, first.PayloadFormat)
            || !Comparer.Equals(candidate.AdmissionPolicy, first.AdmissionPolicy)
            || !Comparer.Equals(candidate.ParserDomain, first.ParserDomain));

        if (conflict is not null)
        {
            throw new ExecutionPlanCompilationException(
                $"physical resource '{group.Key}' has conflicting acquisition settings between profiles '{first.ProfileId}' and '{conflict.ProfileId}'");
        }

        return new ResourceAcquisitionPlan(
            group.Key,
            first.Kind,
            first.Framing,
            first.PayloadFormat,
            first.AdmissionPolicy,
            first.ParserDomain,
            candidates.Select(candidate => candidate.ProfileId).ToArray());
    }

    private static string BuildAcquisitionKey(ResourceDescriptor resource, string kind)
    {
        var builder = new StringBuilder();
        Append(builder, "kind", kind);
        Append(builder, "platform", resource.Platform);
        Append(builder, "mode", resource.Mode);
        Append(builder, "channel", resource.Channel);
        Append(builder, "provider", resource.Provider);
        Append(builder, "providerGuid", resource.ProviderGuid?.ToString("D"));
        Append(builder, "scope", resource.Scope);
        Append(builder, "service", resource.Service);
        Append(builder, "session", resource.Session);

        foreach (var recordType in resource.RecordTypes.Order(StringComparer.OrdinalIgnoreCase))
        {
            Append(builder, "recordType", recordType);
        }

        foreach (var option in resource.Options
            .Where(option => !PlanningOptionKeys.Contains(option.Key, StringComparer.OrdinalIgnoreCase))
            .OrderBy(option => option.Key, StringComparer.OrdinalIgnoreCase))
        {
            Append(builder, $"option:{option.Key}", option.Value?.ToString());
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append('|');
        }

        builder.Append(key.ToLowerInvariant());
        builder.Append('=');
        builder.Append(NormalizeToken(value));
    }

    private static string? ReadOption(ResourceDescriptor resource, string key) =>
        resource.Options.TryGetValue(key, out var value) && value is not null
            ? value.ToString()
            : null;

    private static string InferFraming(string kind, string mode) => kind switch {
        "syslog-tcp" => "rfc6587-or-newline",
        "syslog-udp" => "datagram",
        "syslog-forwarder" => "forward-session",
        "fifo" => "line",
        "file" => Comparer.Equals(mode, "tail") ? "line-tail" : "line",
        _ => "native"
    };

    private static string InferPayloadFormat(string schema, string kind)
    {
        if (!string.IsNullOrWhiteSpace(schema))
        {
            return schema;
        }

        return kind switch {
            "csv" => "csv",
            "eventlog" or "evtx" or "etl" or "etw" => "structured",
            "messagepack" or "forwarder-messagepack" => "messagepack-deliverybatch",
            _ => "text"
        };
    }

    private static string InferAdmission(string kind, string payloadFormat) =>
        payloadFormat.Equals("text", StringComparison.OrdinalIgnoreCase) && kind.StartsWith("syslog", StringComparison.OrdinalIgnoreCase)
            ? "syslog-pri"
            : "default";

    private static string InferParserDomain(string kind, string payloadFormat) =>
        payloadFormat.Equals("text", StringComparison.OrdinalIgnoreCase)
            ? kind
            : "structured";

    private static string NormalizeRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ExecutionPlanCompilationException($"{name} is required");
        }

        return NormalizeToken(value);
    }

    private static string NormalizeToken(string value) => value.Trim().ToLowerInvariant();

    private sealed record Candidate(
        string ProfileId,
        string AcquisitionKey,
        string Kind,
        string Framing,
        string PayloadFormat,
        string AdmissionPolicy,
        string ParserDomain);
}
