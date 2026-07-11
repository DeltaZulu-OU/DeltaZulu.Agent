using System.Reflection;
using DeltaZulu.Pipeline.Inputs.Etw;
using Microsoft.Diagnostics.Tracing;

namespace DeltaZulu.Pipeline.Inputs.Windows;

internal static class TraceEventSourceEventMapper
{
    private static readonly IEtwPayloadMaterializer PayloadMaterializer = new TraceEventPayloadMaterializer();

    // Keep this mapper limited to the ETW envelope and provider payload fields.
    // Resolver, enrichment, and normalized DeltaZulu fields must be added by an
    // explicit schema extension with provenance metadata.
    public static IReadOnlyDictionary<string, object?> ToDictionary(
        TraceEvent data,
        IReadOnlySet<string>? selectedPayloadFields = null) => ToDictionary(data, selectedPayloadFields, out _);

    public static IReadOnlyDictionary<string, object?> ToDictionary(
        TraceEvent data,
        IReadOnlySet<string>? selectedPayloadFields,
        out EtwPayloadMaterializationResult payloadMaterialization)
    {
        var envelope = ToEnvelope(data);
        var fields = new Dictionary<string, object?>(EstimateFieldCapacity(data, selectedPayloadFields), StringComparer.OrdinalIgnoreCase);
        envelope.AddTo(fields);
        fields["EventName"] = data.EventName;

        payloadMaterialization = PayloadMaterializer.AddSelected(data, selectedPayloadFields, fields);

        return fields;
    }

    private static int EstimateFieldCapacity(TraceEvent data, IReadOnlySet<string>? selectedPayloadFields)
    {
        const int EnvelopeFieldCount = 21;
        var payloadFieldCount = selectedPayloadFields is { Count: > 0 }
            ? selectedPayloadFields.Count
            : SafePayloadNames(data).Length;

        return EnvelopeFieldCount + payloadFieldCount;
    }

    internal static NativeEtwEnvelope ToEnvelope(TraceEvent data) => new()
    {
        ProviderGuid = data.ProviderGuid,
        ProviderName = data.ProviderName,
        EventId = (int)data.ID,
        Opcode = (int)data.Opcode,
        OpcodeName = data.OpcodeName,
        Version = data.Version,
        Task = (int)data.Task,
        TaskName = data.TaskName,
        Level = (int)data.Level,
        LevelName = data.Level.ToString(),
        Keywords = (long)data.Keywords,
        TimestampUtc = data.TimeStamp,
        ProcessId = data.ProcessID,
        ThreadId = data.ThreadID,
        ActivityId = data.ActivityID,
        RelatedActivityId = GetOptional<Guid?>(data, "RelatedActivityID", "RelatedActivityId"),
        Channel = GetOptional<int?>(data, "Channel"),
        ProcessorId = GetOptional<int?>(data, "ProcessorNumber", "ProcessorId"),
        TimestampRaw = GetOptional<long?>(data, "TimeStampQPC", "TimeStampRaw"),
        PayloadLength = GetOptional<int?>(data, "EventDataLength", "PayloadLength")
    };

    internal static string[] SafePayloadNames(TraceEvent data)
    {
        try
        {
            return data.PayloadNames;
        }
        catch
        {
            return [];
        }
    }

    private static T? GetOptional<T>(TraceEvent data, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = data.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property is null)
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(data);
            }
            catch
            {
                continue;
            }

            if (value is null)
            {
                return default;
            }

            if (value is T typed)
            {
                return typed;
            }

            try
            {
                var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                return (T)Convert.ChangeType(value, targetType);
            }
            catch
            {
                continue;
            }
        }

        return default;
    }
}
