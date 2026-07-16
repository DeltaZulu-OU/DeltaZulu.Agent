using System.Collections.Concurrent;
using DeltaZulu.Pipeline.Inputs.Etw;
using Microsoft.Diagnostics.Tracing;

namespace DeltaZulu.Pipeline.Inputs.Windows;

internal sealed class EtwPayloadProjectionWarningLimiter
{
    private readonly Action<string>? _warn;
    private readonly string _sourceName;
    private readonly ConcurrentDictionary<ProjectedPayloadFieldIssueKey, byte> _warnedKeys = [];

    public EtwPayloadProjectionWarningLimiter(Action<string>? warn, string sourceName)
    {
        _warn = warn;
        _sourceName = sourceName;
    }

    public void Report(
        TraceEvent data,
        IReadOnlySet<string>? selectedPayloadFields,
        EtwPayloadMaterializationResult payloadMaterialization,
        EtwCollectorMetrics? metrics = null)
    {
        if (selectedPayloadFields is null || selectedPayloadFields.Count == 0)
        {
            return;
        }

        foreach (var payloadField in payloadMaterialization.FailedPayloadFields)
        {
            metrics?.IncrementEtwProjectedPayloadFieldDecodeFailures();
            WarnOnce(
                new ProjectedPayloadFieldIssueKey(data.ProviderGuid, (int)data.ID, data.Version, payloadField, ProjectedPayloadFieldIssueKind.DecodeFailure),
                data.ProviderName,
                metrics);
        }

        foreach (var payloadField in EtwPayloadProjection.SelectMissingPayloadFields(selectedPayloadFields, payloadMaterialization))
        {
            metrics?.IncrementEtwProjectedPayloadFieldMissingOccurrences();
            WarnOnce(
                new ProjectedPayloadFieldIssueKey(data.ProviderGuid, (int)data.ID, data.Version, payloadField, ProjectedPayloadFieldIssueKind.Missing),
                data.ProviderName,
                metrics);
        }
    }

    private void WarnOnce(ProjectedPayloadFieldIssueKey key, string? providerName, EtwCollectorMetrics? metrics)
    {
        if (!_warnedKeys.TryAdd(key, 0))
        {
            return;
        }

        if (key.IssueKind == ProjectedPayloadFieldIssueKind.Missing)
        {
            metrics?.IncrementEtwProjectedPayloadFieldMissingDistinctKeys();
        }

        _warn?.Invoke(
            $"ETW source '{_sourceName}': projected payload field '{key.PayloadField}' {GetIssueDescription(key.IssueKind)} " +
            $"for provider '{providerName ?? key.ProviderGuid.ToString()}' ({key.ProviderGuid}), event ID {key.EventId}, version {key.Version}.");
    }

    private static string GetIssueDescription(ProjectedPayloadFieldIssueKind issueKind) => issueKind switch
    {
        ProjectedPayloadFieldIssueKind.DecodeFailure => "was present but could not be decoded",
        _ => "was not present"
    };

    private enum ProjectedPayloadFieldIssueKind
    {
        Missing,
        DecodeFailure
    }

    private readonly record struct ProjectedPayloadFieldIssueKey(
        Guid ProviderGuid,
        int EventId,
        int Version,
        string PayloadField,
        ProjectedPayloadFieldIssueKind IssueKind);
}
