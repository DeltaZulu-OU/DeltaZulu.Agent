using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DeltaZulu.Pipeline.Enrichment;

public static class EtwEventEnricher
{
    public const string ResolverVersion = "DeltaZulu.Etw.Deterministic/1.2";

    public static IReadOnlyDictionary<string, object?>? BuildEtwEnrichment(
        IReadOnlyDictionary<string, object?> fields,
        IReadOnlyDictionary<string, object?> metadata)
    {
        if (!IsWindowsEtw(metadata, fields))
        {
            return null;
        }

        var enrichment = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        AddIfNotEmpty(enrichment, "Etw", BuildEtwIdentity(fields));
        AddIfNotEmpty(enrichment, "Process", BuildProcessContext(fields));
        AddIfNotEmpty(enrichment, "Thread", BuildThreadContext(fields));
        AddIfNotEmpty(enrichment, "Network", BuildNetworkContext(fields));
        AddIfNotEmpty(enrichment, "DotNet", BuildDotNet(fields));
        AddIfNotEmpty(enrichment, "Timing", BuildTiming(fields, metadata));
        AddIfNotEmpty(enrichment, "Quality", BuildQuality(fields));

        return enrichment.Count == 0 ? null : enrichment;
    }

    private static void AddIfNotEmpty(IDictionary<string, object?> target, string key, IReadOnlyDictionary<string, object?>? value)
    {
        if (value is not null && value.Count > 0)
        {
            target[key] = value;
        }
    }

    private static bool IsWindowsEtw(IReadOnlyDictionary<string, object?> metadata, IReadOnlyDictionary<string, object?> fields) =>
        StringEquals(metadata, "sourceType", "WindowsEtw") ||
        (!string.IsNullOrWhiteSpace(FirstString(fields, "ProviderName")) && HasAny(fields, "ProviderGuid", "EventId", "EventName", "ProcessId", "ThreadId"));

    private static IReadOnlyDictionary<string, object?> BuildEtwIdentity(IReadOnlyDictionary<string, object?> fields)
    {
        var etw = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ResolverVersion"] = ResolverVersion
        };

        CopyNonEmpty(fields, etw, "ProviderGuid", "ProviderName", "EventId", "EventName", "Opcode", "OpcodeName", "Task", "TaskName", "LevelCode", "Level", "Keywords", "Version", "ProcessorId", "Channel", "PayloadLength");

        var provider = FirstString(fields, "ProviderName");
        if (provider?.Equals("Microsoft-Windows-DotNETRuntime", StringComparison.OrdinalIgnoreCase) == true)
        {
            etw["ProviderCategory"] = ".NET Runtime";
            etw["RuntimeFamily"] = "DotNET";
        }
        else if (provider?.Equals("Microsoft-Windows-Kernel-Network", StringComparison.OrdinalIgnoreCase) == true)
        {
            etw["ProviderCategory"] = "Kernel Network";
            etw["EventDomain"] = "Network";
        }

        var eventCategory = FirstNonEmpty(FirstString(fields, "TaskName"), CategoryFromEventName(FirstString(fields, "EventName")));
        if (eventCategory is not null)
        {
            etw["EventCategory"] = eventCategory;
        }

        return etw;
    }

    private static IReadOnlyDictionary<string, object?>? BuildProcessContext(IReadOnlyDictionary<string, object?> fields)
    {
        var processId = FirstInt(fields, "ProcessId", "ProcessID", "Pid");
        var image = FirstString(fields, "Image", "ProcessImage", "ResolvedProcessImage");
        var commandLine = FirstString(fields, "CommandLine", "ProcessCommandLine", "ResolvedProcessCommandLine");
        var parentImage = FirstString(fields, "ParentImage", "ParentProcessImage");
        var processName = FirstString(fields, "ProcessName", "ImageFileName");
        var source = "EtwPayload";
        var confidence = "Medium";
        var startTimeUtc = FirstDateTimeOffset(fields, "ProcessStartTime", "StartTimeUtc");

        if (processId is null)
        {
            return null;
        }

        if (image is null && commandLine is null && parentImage is null && processName is null)
        {
            var snapshot = TryResolveLiveProcess(processId.Value);
            if (snapshot is null)
            {
                return null;
            }

            image = snapshot.ImagePath;
            commandLine = snapshot.CommandLine;
            processName = snapshot.ProcessName;
            startTimeUtc = snapshot.StartTimeUtc;
            source = snapshot.ResolutionSource;
            confidence = snapshot.ResolutionConfidence;
        }

        var process = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProcessId"] = processId,
            ["ProcessIdentityStatus"] = "ObservedContext",
            ["ProcessResolutionSource"] = source,
            ["ProcessResolutionConfidence"] = confidence
        };
        AddIfNotBlank(process, "ProcessName", processName);
        AddIfNotBlank(process, "Image", image);
        AddIfNotBlank(process, "CommandLine", commandLine);
        AddIfNotBlank(process, "ParentImage", parentImage);
        if (startTimeUtc is not null)
        {
            process["ProcessStartTimeUtc"] = startTimeUtc.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
            process["ProcessGenerationKey"] = $"ProcessId={processId};StartTimeUtc={startTimeUtc.Value.UtcDateTime:O}";
        }
        else
        {
            process["ProcessGenerationKey"] = $"ProcessId={processId}";
        }

        return process;
    }

    private sealed record LiveProcessSnapshot(
        string? ProcessName,
        string? ImagePath,
        string? CommandLine,
        DateTimeOffset? StartTimeUtc,
        string ResolutionSource,
        string ResolutionConfidence);

    private static LiveProcessSnapshot? TryResolveLiveProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var processName = process.ProcessName;
            var imagePath = TryGetMainModuleFileName(process);
            var commandLine = processId == Environment.ProcessId ? Environment.CommandLine : null;
            var startTimeUtc = TryGetStartTimeUtc(process);

            if (string.IsNullOrWhiteSpace(processName) && string.IsNullOrWhiteSpace(imagePath) && string.IsNullOrWhiteSpace(commandLine))
            {
                return null;
            }

            return new LiveProcessSnapshot(
                processName,
                imagePath,
                commandLine,
                startTimeUtc,
                "LocalProcessSnapshot",
                startTimeUtc is null ? "Low" : "Medium");
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? TryGetMainModuleFileName(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static DateTimeOffset? TryGetStartTimeUtc(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime).ToUniversalTime();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, object?>? BuildThreadContext(IReadOnlyDictionary<string, object?> fields)
    {
        var threadId = FirstInt(fields, "ThreadId", "ThreadID", "Tid");
        if (threadId is null || !HasAny(fields, "ThreadState", "ThreadWaitReason", "ThreadWaitCategory"))
        {
            return null;
        }

        var thread = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ThreadId"] = threadId,
            ["ThreadIdentityStatus"] = "ObservedContext"
        };
        CopyNonEmpty(fields, thread, "ThreadState", "ThreadWaitReason", "ThreadWaitCategory", "ThreadWasInIoWait");
        return thread;
    }

    private static IReadOnlyDictionary<string, object?>? BuildNetworkContext(IReadOnlyDictionary<string, object?> fields)
    {
        var localIp = FirstString(fields, "LocalIp", "LocalAddress");
        var remoteIp = FirstString(fields, "RemoteIp", "RemoteAddress");
        var localPort = FirstString(fields, "LocalPort");
        var remotePort = FirstString(fields, "RemotePort");
        var protocol = FirstString(fields, "Protocol");
        if (localIp is null && remoteIp is null && localPort is null && remotePort is null && protocol is null)
        {
            return null;
        }

        var network = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ResolverVersion"] = ResolverVersion
        };
        AddIfNotBlank(network, "LocalIp", localIp);
        AddIfNotBlank(network, "RemoteIp", remoteIp);
        AddIfNotBlank(network, "LocalPort", localPort);
        AddIfNotBlank(network, "RemotePort", remotePort);
        AddIfNotBlank(network, "Protocol", protocol);
        return network;
    }

    private static IReadOnlyDictionary<string, object?>? BuildDotNet(IReadOnlyDictionary<string, object?> fields)
    {
        var provider = FirstString(fields, "ProviderName");
        var exceptionType = FirstString(fields, "ExceptionType");
        var clrInstanceId = FirstInt(fields, "ClrInstanceID", "ClrInstanceId");
        var isDotNetProvider = provider?.Equals("Microsoft-Windows-DotNETRuntime", StringComparison.OrdinalIgnoreCase) == true;
        if (!isDotNetProvider && exceptionType is null && clrInstanceId is null)
        {
            return null;
        }

        var dotNet = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["RuntimeFamily"] = "DotNET",
            ["IsManagedException"] = exceptionType is not null,
            ["ResolverVersion"] = ResolverVersion
        };
        AddIfNotBlank(dotNet, "RuntimeProvider", provider);
        if (clrInstanceId is not null)
        {
            dotNet["ClrInstanceID"] = clrInstanceId;
        }

        if (exceptionType is not null)
        {
            dotNet["ExceptionType"] = exceptionType;
            var lastDot = exceptionType.LastIndexOf('.');
            dotNet["ExceptionNamespace"] = lastDot > 0 ? exceptionType[..lastDot] : null;
            dotNet["ExceptionClass"] = lastDot >= 0 && lastDot < exceptionType.Length - 1 ? exceptionType[(lastDot + 1)..] : exceptionType;
            dotNet["ExceptionCategory"] = ClassifyException(exceptionType);
            dotNet["ExceptionTypeFingerprint"] = Sha256Hex(exceptionType.ToUpperInvariant());
        }

        var message = FirstString(fields, "ExceptionMessage");
        if (message is not null)
        {
            dotNet["ExceptionMessageFingerprint"] = Sha256Hex(NormalizeExceptionMessage(message));
        }

        var hresult = FirstLong(fields, "ExceptionHRESULT", "ExceptionHResult");
        if (hresult is not null)
        {
            dotNet["ExceptionHRESULT"] = hresult;
            dotNet["ExceptionHRESULTHex"] = unchecked((uint)hresult.Value).ToString("X8", CultureInfo.InvariantCulture).Insert(0, "0x");
            var symbol = ResolveHResultSymbol(unchecked((uint)hresult.Value));
            if (symbol is not null)
            {
                dotNet["ExceptionHRESULTSymbol"] = symbol;
            }
        }

        CopyNonEmpty(fields, dotNet, "ExceptionFlags", "ExceptionEIP");
        dotNet["ExceptionGroupKey"] = Sha256Hex(string.Join('|', provider, FirstInt(fields, "EventId"), exceptionType, dotNet.GetValueOrDefault("ExceptionMessageFingerprint")));
        return dotNet;
    }

    private static IReadOnlyDictionary<string, object?>? BuildTiming(IReadOnlyDictionary<string, object?> fields, IReadOnlyDictionary<string, object?> metadata)
    {
        var eventTime = FirstDateTimeOffset(fields, "TimeStamp", "Timestamp", "TimeCreated");
        if (eventTime is null)
        {
            return null;
        }

        var timing = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["TimestampSource"] = "ETW Event Timestamp",
            ["EventTimeUtc"] = eventTime.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
        };

        var ingestedAt = FirstDateTimeOffset(metadata, "ingestedAt");
        var relpCreatedAt = TryGetNestedDateTimeOffset(metadata, ["relp", "createdAt"]);
        if (ingestedAt is not null)
        {
            timing["IngestDelayMs"] = (long)(ingestedAt.Value - eventTime.Value).TotalMilliseconds;
        }

        if (relpCreatedAt is not null)
        {
            timing["RelpCreateDelayMs"] = (long)(relpCreatedAt.Value - eventTime.Value).TotalMilliseconds;
        }

        if (relpCreatedAt is not null && ingestedAt is not null)
        {
            timing["RelpToIngestDelayMs"] = (long)(ingestedAt.Value - relpCreatedAt.Value).TotalMilliseconds;
        }

        if (timing.TryGetValue("IngestDelayMs", out var delay) && delay is long delayMs)
        {
            timing["TimestampSkewStatus"] = delayMs < -5000 ? "ClockSkewSuspected" : delayMs > 300000 ? "Delayed" : "Normal";
        }

        return timing;
    }

    private static IReadOnlyDictionary<string, object?>? BuildQuality(IReadOnlyDictionary<string, object?> fields)
    {
        var quality = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (HasAny(fields, "ActivityId", "RelatedActivityId"))
        {
            quality["HasActivityId"] = HasNonZeroGuid(FirstString(fields, "ActivityId"));
            quality["HasRelatedActivityId"] = HasNonZeroGuid(FirstString(fields, "RelatedActivityId"));
        }

        var payloadLength = FirstInt(fields, "PayloadLength");
        if (payloadLength is not null)
        {
            quality["PayloadLength"] = payloadLength;
        }

        if (quality.Count > 0)
        {
            quality["ResolverVersion"] = ResolverVersion;
        }

        return quality.Count == 0 ? null : quality;
    }

    private static string? CategoryFromEventName(string? eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return null;
        }

        var slash = eventName.IndexOf('/');
        var category = slash > 0 ? eventName[..slash] : eventName;
        return category.TrimEnd('.');
    }

    private static string ClassifyException(string exceptionType) => exceptionType.EndsWith("InvalidCastException", StringComparison.Ordinal)
        ? "TypeConversion"
        : exceptionType.StartsWith("System.", StringComparison.Ordinal) ? "SystemRuntimeException" : "ManagedException";

    private static string? ResolveHResultSymbol(uint hresult) => hresult switch
    {
        0x80004002 => "E_NOINTERFACE",
        0x80004003 => "E_POINTER",
        0x80004005 => "E_FAIL",
        0x80070005 => "E_ACCESSDENIED",
        0x8007000E => "E_OUTOFMEMORY",
        0x80070057 => "E_INVALIDARG",
        _ => null
    };

    private static string NormalizeExceptionMessage(string message) => string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string Sha256Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void CopyNonEmpty(IReadOnlyDictionary<string, object?> source, IDictionary<string, object?> target, params string[] names)
    {
        foreach (var name in names)
        {
            if (source.TryGetValue(name, out var value) && IsMeaningful(value))
            {
                target[name] = value;
            }
        }
    }

    private static void AddIfNotBlank(IDictionary<string, object?> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }

    private static bool IsMeaningful(object? value) => value switch
    {
        null => false,
        string text => !string.IsNullOrWhiteSpace(text),
        _ => true
    };

    private static bool HasAny(IReadOnlyDictionary<string, object?> fields, params string[] names) => names.Any(fields.ContainsKey);

    private static bool StringEquals(IReadOnlyDictionary<string, object?> fields, string name, string expected) =>
        fields.TryGetValue(name, out var value) && value?.ToString()?.Equals(expected, StringComparison.OrdinalIgnoreCase) == true;

    private static string? FirstString(IReadOnlyDictionary<string, object?> fields, params string[] names)
    {
        foreach (var name in names)
        {
            if (fields.TryGetValue(name, out var value) && value is not null)
            {
                var text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static int? FirstInt(IReadOnlyDictionary<string, object?> fields, params string[] names)
    {
        var value = FirstLong(fields, names);
        return value is <= int.MaxValue and >= int.MinValue ? (int)value.Value : null;
    }

    private static long? FirstLong(IReadOnlyDictionary<string, object?> fields, params string[] names)
    {
        foreach (var name in names)
        {
            if (!fields.TryGetValue(name, out var value) || value is null)
            {
                continue;
            }

            switch (value)
            {
                case long longValue:
                    return longValue;
                case int intValue:
                    return intValue;
                case short shortValue:
                    return shortValue;
                case byte byteValue:
                    return byteValue;
                case string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                    return parsed;
            }
        }

        return null;
    }

    private static DateTimeOffset? FirstDateTimeOffset(IReadOnlyDictionary<string, object?> fields, params string[] names)
    {
        foreach (var name in names)
        {
            if (!fields.TryGetValue(name, out var value) || value is null)
            {
                continue;
            }

            switch (value)
            {
                case DateTimeOffset dto:
                    return dto;
                case DateTime dt:
                    return new DateTimeOffset(dt);
                case string text when !string.IsNullOrWhiteSpace(text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed):
                    return parsed;
            }
        }

        return null;
    }

    private static DateTimeOffset? TryGetNestedDateTimeOffset(IReadOnlyDictionary<string, object?> fields, IReadOnlyList<string> path)
    {
        object? current = fields;
        foreach (var segment in path)
        {
            if (current is IReadOnlyDictionary<string, object?> dict && dict.TryGetValue(segment, out var value))
            {
                current = value;
                continue;
            }

            if (current is IDictionary<string, object?> mutableDict && mutableDict.TryGetValue(segment, out value))
            {
                current = value;
                continue;
            }

            return null;
        }

        return current switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt),
            string text when !string.IsNullOrWhiteSpace(text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) => parsed,
            _ => null
        };
    }

    private static bool HasNonZeroGuid(string? value) => Guid.TryParse(value, out var guid) && guid != Guid.Empty;
}
