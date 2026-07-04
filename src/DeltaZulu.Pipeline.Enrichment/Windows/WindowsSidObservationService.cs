using System.Text.Json;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Enrichment.Windows;

public sealed class WindowsSidObservationService
{
    private readonly string _cachePath;
    private readonly IWindowsSidResolver _resolver;
    private readonly WindowsSidObservationOptions _options;
    private readonly Dictionary<string, SidResolutionResult> _cache = new(StringComparer.OrdinalIgnoreCase);

    public WindowsSidObservationService(string cachePath, IWindowsSidResolver resolver, WindowsSidObservationOptions? options = null)
    {
        _cachePath = cachePath;
        _resolver = resolver;
        _options = options ?? new WindowsSidObservationOptions();
        LoadCache();
    }

    public IReadOnlyList<SidObservation> Observe(SourceEvent source, IDictionary<string, object?> projectedFields)
    {
        var observations = new List<SidObservation>();
        if (!IsRelevantWindowsSecurityEvent(source))
        {
            return observations;
        }

        var observedAt = GetObservedAt(projectedFields);
        var sourceEventId = GetEventId(projectedFields.AsReadOnly());
        var isDeletion = sourceEventId == 4726;

        foreach (var (sidField, sid) in GetProjectedSidFields(projectedFields))
        {
            var resolution = GetOrResolve(sid, observedAt, projectedFields, sidField);
            if (isDeletion)
            {
                resolution = resolution with { LifecycleStatus = "DeletedObserved", DeletedAtUtc = observedAt };
                _cache[sid] = resolution;
                SaveCache();
            }

            observations.Add(ToObservation(source, projectedFields, resolution, sourceEventId));
        }

        return observations;
    }

    private SidResolutionResult GetOrResolve(string sid, DateTimeOffset observedAt, IDictionary<string, object?> fields, string sidField)
    {
        if (TryBuildFromEventPayload(sid, observedAt, fields, sidField, out var eventPayload))
        {
            _cache[sid] = eventPayload;
            SaveCache();
            return eventPayload;
        }

        if (_cache.TryGetValue(sid, out var cached) && cached.CacheExpiresAtUtc > observedAt)
        {
            return cached with { ResolutionSource = "Sid" };
        }

        var resolved = _resolver.Resolve(sid, observedAt, _options.CacheTtl);
        if (resolved.ResolutionStatus is "Resolved" or "Partial")
        {
            _cache[sid] = resolved;
            SaveCache();
        }

        return resolved;
    }

    private bool TryBuildFromEventPayload(string sid, DateTimeOffset observedAt, IDictionary<string, object?> fields, string sidField, out SidResolutionResult result)
    {
        result = default!;
        if (!TryGetCompanionField(fields.AsReadOnly(), sidField, "Name", out var name) || IsUnresolvedName(name))
        {
            return false;
        }

        string? domain = null;
        if (TryGetCompanionField(fields.AsReadOnly(), sidField, "DomainName", out var domainValue) && !IsUnresolvedName(domainValue))
        {
            domain = domainValue;
        }

        result = new SidResolutionResult(sid, name, domain, string.IsNullOrWhiteSpace(domain) ? name : $"{domain}\\{name}", "Unknown", "Unknown", "EventPayload", "Resolved", "Medium", observedAt, observedAt.Add(_options.CacheTtl));
        return true;
    }

    private SidObservation ToObservation(SourceEvent source, IDictionary<string, object?> fields, SidResolutionResult resolution, int? sourceEventId) => new() {
        TenantId = GetMetadata(source, "tenantId"),
        DeviceId = GetMetadata(source, "deviceId"),
        AgentId = GetMetadata(source, "agentId") ?? source.Metadata.CollectorId,
        BootId = GetMetadata(source, "bootId"),
        ObservedAtUtc = resolution.ObservedAtUtc,
        Sid = resolution.Sid,
        ResolvedAccountName = resolution.AccountName,
        ResolvedDomainName = resolution.DomainName,
        ResolvedCanonicalName = resolution.CanonicalName,
        PrincipalType = resolution.PrincipalType,
        SidScope = resolution.SidScope,
        ResolutionSource = resolution.ResolutionSource,
        ResolutionStatus = resolution.ResolutionStatus,
        ResolutionConfidence = resolution.ResolutionConfidence,
        SourceRecordId = TryGetString(fields.AsReadOnly(), "RecordId", out var recordId) ? recordId : null,
        SourceEventId = sourceEventId,
        SourceProvider = TryGetString(fields.AsReadOnly(), "ProviderName", out var provider) ? provider : source.Metadata.SourceName,
        LifecycleStatus = resolution.LifecycleStatus,
        DeletedAtUtc = resolution.DeletedAtUtc,
        CacheExpiresAtUtc = resolution.CacheExpiresAtUtc
    };

    private bool IsRelevantWindowsSecurityEvent(SourceEvent source)
    {
        if (!string.Equals(source.Metadata.SourceType, "WindowsEventLog", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(source.Metadata.SourceType, "WindowsEtw", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fields = source.Fields;
        var providerOk = TryGetString(fields, "ProviderName", out var provider) && string.Equals(provider, "Microsoft-Windows-Security-Auditing", StringComparison.OrdinalIgnoreCase);
        var securityChannel = string.Equals(source.Metadata.SourceName, "Security", StringComparison.OrdinalIgnoreCase);
        return (providerOk || securityChannel) && GetEventId(fields) is int id && _options.PriorityEventIds.Contains(id);
    }

    private void LoadCache()
    {
        if (!File.Exists(_cachePath))
        {
            return;
        }

        var items = JsonSerializer.Deserialize<List<SidResolutionResult>>(File.ReadAllText(_cachePath)) ?? [];
        foreach (var item in items)
        {
            _cache[item.Sid] = item;
        }
    }

    private void SaveCache()
    {
        var dir = Path.GetDirectoryName(_cachePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_cachePath, JsonSerializer.Serialize(_cache.Values));
    }

    private static IEnumerable<(string Field, string Sid)> GetProjectedSidFields(IDictionary<string, object?> fields)
    {
        foreach (var (key, value) in fields)
        {
            var sid = Convert.ToString(value) ?? string.Empty;
            if (key.EndsWith("Sid", StringComparison.OrdinalIgnoreCase) && LooksLikeSid(sid))
            {
                yield return (key, sid);
            }
        }
    }

    private static bool TryGetCompanionField(IReadOnlyDictionary<string, object?> fields, string sidField, string suffix, out string value)
    {
        var stem = sidField.EndsWith("Sid", StringComparison.OrdinalIgnoreCase) ? sidField[..^3] : sidField;
        if (TryGetString(fields, $"{stem}{suffix}", out value))
        {
            return true;
        }

        if (stem.EndsWith("User", StringComparison.OrdinalIgnoreCase) && TryGetString(fields, $"{stem[..^4]}{suffix}", out value))
        {
            return true;
        }

        return false;
    }

    private static bool IsUnresolvedName(string? value) => string.IsNullOrWhiteSpace(value) || value.Trim() == "-";

    private static bool LooksLikeSid(string value) => value.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetString(IReadOnlyDictionary<string, object?> fields, string key, out string value)
    { if (fields.TryGetValue(key, out var v) && v is not null) { value = Convert.ToString(v) ?? string.Empty; return true; } value = string.Empty; return false; }

    private static int? GetEventId(IReadOnlyDictionary<string, object?> fields) => TryGetString(fields, "EventId", out var v) || TryGetString(fields, "EventID", out v) ? int.TryParse(v, out var id) ? id : null : null;

    private static DateTimeOffset GetObservedAt(IDictionary<string, object?> fields) => TryGetString(fields.AsReadOnly(), "TimeCreated", out var t) && DateTimeOffset.TryParse(t, out var dto) ? dto.ToUniversalTime() : DateTimeOffset.UtcNow;

    private static string? GetMetadata(SourceEvent source, string key) => source.Metadata.Properties.TryGetValue(key, out var value) ? Convert.ToString(value) : null;
}