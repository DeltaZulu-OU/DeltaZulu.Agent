using DeltaZulu.Pipeline.Core;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Checkpoints;
using DeltaZulu.Pipeline.Core.Windows;
using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;
using System.Reactive.Disposables;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Inputs.Windows;

public sealed class WindowsEventLogInput : ISourceInput
{
    public const string DisabledChannelErrorFragment = "exists on this host but its channel is disabled";
    private readonly string _requestedLogName;
    private readonly TimeSpan _pollInterval;
    private readonly PollRetryPolicy _retryPolicy;
    private readonly EventLogStartPosition _startPosition;
    private readonly long? _configuredRecordId;
    private readonly TimeSpan? _lookback;
    private readonly ISourceCheckpointStore _checkpointStore;
    private readonly string _checkpointKey;
    private string? _resolvedLogName;
    private int _subscribed;
    public string Name { get; }

    public WindowsEventLogInput(
        string logName,
        string? name = null,
        TimeSpan? pollInterval = null,
        PollRetryPolicy? retryPolicy = null,
        EventLogStartPosition startPosition = EventLogStartPosition.FromNow,
        long? recordId = null,
        TimeSpan? lookback = null,
        ISourceCheckpointStore? checkpointStore = null,
        string? checkpointKey = null)
    {
        _requestedLogName = logName;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        _retryPolicy = retryPolicy ?? new PollRetryPolicy();
        _startPosition = startPosition;
        _configuredRecordId = recordId;
        _lookback = lookback;
        _checkpointStore = checkpointStore ?? NullSourceCheckpointStore.Instance;
        Name = name ?? logName;
        _checkpointKey = checkpointKey ?? $"windows.eventlog.{Name}";
    }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default) => System.Reactive.Linq.Observable.Create<SourceEvent>(observer => {
        // Guard against a second concurrent subscription starting its own poll loop with an
        // independent record cursor on the same instance. Only one active subscription is allowed.
        if (Interlocked.CompareExchange(ref _subscribed, 1, 0) != 0)
        {
            observer.OnError(new InvalidOperationException($"Windows Event Log input '{Name}' is already open; each instance supports a single active subscription."));
            return Disposable.Empty;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => PollAsync(observer, cts.Token), cts.Token);
        return Disposable.Create(() => {
            cts.Cancel();
            cts.Dispose();
            Interlocked.Exchange(ref _subscribed, 0);
        });
    });

    private async Task PollAsync(IObserver<SourceEvent> observer, CancellationToken cancellationToken)
    {
        try
        {
            if (!TryResolveLogName(out var logName, out var errorMessage))
            {
                // A missing/disabled/inaccessible channel is a configuration error, not a transient
                // read failure, so it terminates the stream immediately rather than being retried.
                observer.OnError(new InvalidOperationException(errorMessage ?? $"Unable to resolve Windows Event Log '{_requestedLogName}'."));
                return;
            }

            long lastRecordId = 0;
            long lastPersisted = -1;
            var initialized = false;
            var consecutiveFailures = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!initialized)
                    {
                        lastRecordId = InitializeCursor(observer, logName);
                        initialized = true;
                    }

                    lastRecordId = ReadNewEvents(observer, logName, lastRecordId);

                    // Persist the resume point only after the batch's synchronous handoff returns.
                    // Because OnNext blocks until each record reaches the durable buffer, this
                    // coincides with the durable-enqueue advancement boundary in the current pipeline.
                    if (lastRecordId != lastPersisted)
                    {
                        _checkpointStore.Save(_checkpointKey, lastRecordId.ToString());
                        lastPersisted = lastRecordId;
                    }

                    consecutiveFailures = 0;
                    await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (IsTransientReadError(ex))
                {
                    // Transient read failures (channel momentarily locked, provider metadata not yet
                    // available) must not kill the stream. Back off and retry until the failure count
                    // crosses the escalation threshold, at which point the channel is treated as broken.
                    consecutiveFailures++;
                    if (_retryPolicy.ShouldEscalate(consecutiveFailures))
                    {
                        observer.OnError(CreatePollingException(ex, consecutiveFailures));
                        return;
                    }

                    await Task.Delay(_retryPolicy.GetBackoffDelay(consecutiveFailures), cancellationToken).ConfigureAwait(false);
                }
            }

            observer.OnCompleted();
        }
        catch (OperationCanceledException)
        {
            observer.OnCompleted();
        }
        catch (Exception ex)
        {
            observer.OnError(CreatePollingException(ex));
        }
    }

    private static bool IsTransientReadError(Exception exception) =>
        exception is EventLogException or Win32Exception;

    private Exception CreatePollingException(Exception exception, int? consecutiveFailures = null)
    {
        var logName = string.IsNullOrWhiteSpace(_resolvedLogName) ? _requestedLogName : _resolvedLogName;
        var suffix = consecutiveFailures is { } count
            ? $" after {count} consecutive read failures"
            : string.Empty;
        return new InvalidOperationException($"Windows Event Log input '{Name}' failed while reading '{logName}'{suffix}: {exception.Message}", exception);
    }

    private long InitializeCursor(IObserver<SourceEvent> observer, string logName)
    {
        var token = _checkpointStore.TryLoad(_checkpointKey, out var loaded) ? loaded : null;
        var resolved = EventLogStartPositionResolver.Resolve(_startPosition, _configuredRecordId, _lookback, token);

        return resolved.Kind switch
        {
            ResolvedStartKind.Newest => ReadLatestRecordId(logName),
            ResolvedStartKind.Oldest => 0,
            ResolvedStartKind.AfterRecordId => resolved.RecordId,
            ResolvedStartKind.Lookback => SeekLookback(observer, logName, resolved.Lookback),
            _ => ReadLatestRecordId(logName)
        };
    }

    private long SeekLookback(IObserver<SourceEvent> observer, string logName, TimeSpan window)
    {
        var windowMs = (long)Math.Max(0, window.TotalMilliseconds);
        var queryText = $"*[System[TimeCreated[timediff(@SystemTime) <= {windowMs}]]]";
        var query = new EventLogQuery(logName, PathType.LogName, queryText);
        using var reader = new EventLogReader(query);

        var newestRecordId = 0L;
        for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
        {
            using (record)
            {
                if (record.RecordId is long recordId)
                {
                    newestRecordId = Math.Max(newestRecordId, recordId);
                }

                observer.OnNext(WindowsSourceEventMapper.FromDictionary(ToDictionary(record), "WindowsEventLog", logName, nameof(WindowsEventLogInput)));
            }
        }

        // No events in the window: start from the current tail rather than replaying the whole channel.
        return newestRecordId > 0 ? newestRecordId : ReadLatestRecordId(logName);
    }

    private long ReadLatestRecordId(string logName)
    {
        var query = new EventLogQuery(logName, PathType.LogName)
        {
            ReverseDirection = true
        };

        using var reader = new EventLogReader(query);
        using var record = reader.ReadEvent();
        return record?.RecordId ?? 0;
    }

    private long ReadNewEvents(IObserver<SourceEvent> observer, string logName, long lastRecordId)
    {
        var queryText = lastRecordId > 0 ? $"*[System[EventRecordID > {lastRecordId}]]" : "*";
        var query = new EventLogQuery(logName, PathType.LogName, queryText);
        using var reader = new EventLogReader(query);

        var newestRecordId = lastRecordId;
        for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
        {
            using (record)
            {
                if (record.RecordId is long recordId)
                {
                    newestRecordId = Math.Max(newestRecordId, recordId);
                }

                observer.OnNext(WindowsSourceEventMapper.FromDictionary(ToDictionary(record), "WindowsEventLog", logName, nameof(WindowsEventLogInput)));
            }
        }

        return newestRecordId;
    }

    private bool TryResolveLogName(out string logName, out string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(_resolvedLogName))
        {
            logName = _resolvedLogName;
            errorMessage = null;
            return true;
        }

        if (TryResolveLogName(_requestedLogName, out logName, out errorMessage))
        {
            _resolvedLogName = logName;
            return true;
        }

        return false;
    }

    public static bool TryValidateLogReadable(string requestedLogName, out string logName, out string? errorMessage)
    {
        if (!TryResolveLogName(requestedLogName, out logName, out errorMessage))
        {
            return false;
        }

        try
        {
            var query = new EventLogQuery(logName, PathType.LogName)
            {
                ReverseDirection = true
            };
            using var reader = new EventLogReader(query);
            using var record = reader.ReadEvent();
            errorMessage = null;
            return true;
        }
        catch (EventLogException ex)
        {
            errorMessage = $"Unable to read Windows Event Log '{logName}': {ex.Message}";
            return false;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            errorMessage = $"Unable to read Windows Event Log '{logName}': {ex.Message}";
            return false;
        }
    }

    public static bool TryResolveLogName(string requestedLogName, out string logName, out string? errorMessage)
    {
        var requested = ExpandLogAlias(requestedLogName);
        try
        {
            using var session = new EventLogSession();
            var logNames = session.GetLogNames().ToList();
            var match = logNames.FirstOrDefault(name => name.Equals(requested, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                if (!IsLogEnabled(match, out errorMessage))
                {
                    logName = match;
                    return false;
                }

                logName = match;
                errorMessage = null;
                return true;
            }

            var suggestions = logNames
                .Where(name => name.Contains(requested, StringComparison.OrdinalIgnoreCase)
                    || requested.Contains(name, StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Sysmon", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Security", StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .ToList();

            logName = requested;
            errorMessage = CreateLogNotFoundMessage(requested, suggestions);
            return false;
        }
        catch (EventLogNotFoundException ex)
        {
            logName = requested;
            errorMessage = CreateLogNotFoundMessage(requested, []) + $" {ex.Message}";
            return false;
        }
        catch (EventLogException ex)
        {
            logName = requested;
            errorMessage = $"Unable to enumerate Windows Event Logs while resolving '{requested}': {ex.Message}";
            return false;
        }
    }

    private static bool IsLogEnabled(string logName, out string? errorMessage)
    {
        try
        {
            using var configuration = new EventLogConfiguration(logName);
            if (configuration.IsEnabled)
            {
                errorMessage = null;
                return true;
            }

            errorMessage = $"Windows Event Log '{logName}' {DisabledChannelErrorFragment}.";
            return false;
        }
        catch (EventLogNotFoundException ex)
        {
            errorMessage = CreateLogNotFoundMessage(logName, []) + $" {ex.Message}";
            return false;
        }
        catch (EventLogException ex)
        {
            errorMessage = $"Unable to inspect Windows Event Log '{logName}' while checking whether the channel is enabled: {ex.Message}";
            return false;
        }
    }

    public static bool IsDisabledChannelError(string? errorMessage)
        => !string.IsNullOrWhiteSpace(errorMessage)
            && errorMessage.Contains(DisabledChannelErrorFragment, StringComparison.OrdinalIgnoreCase);

    private static string ExpandLogAlias(string logName) => logName.ToLowerInvariant() switch
    {
        "security" => "Security",
        "sysmon" => "Microsoft-Windows-Sysmon/Operational",
        "sysmon-operational" => "Microsoft-Windows-Sysmon/Operational",
        "powershell-operational" => "Microsoft-Windows-PowerShell/Operational",
        "powershell-core-operational" => "PowerShellCore/Operational",
        "applocker-exe-and-dll" => "Microsoft-Windows-AppLocker/EXE and DLL",
        "applocker-msi-and-script" => "Microsoft-Windows-AppLocker/MSI and Script",
        _ => logName
    };

    private static string CreateLogNotFoundMessage(string logName, IReadOnlyList<string> suggestions)
    {
        var message = $"Windows Event Log '{logName}' was not found on this host.";
        if (logName.Equals("Microsoft-Windows-Sysmon/Operational", StringComparison.OrdinalIgnoreCase))
        {
            message += " Sysmon must be installed and its Operational channel must be present before querying Event ID 1.";
        }

        if (suggestions.Count > 0)
        {
            message += " Available related logs: " + string.Join(", ", suggestions) + ".";
        }

        return message;
    }

    private static IReadOnlyDictionary<string, object?> ToDictionary(EventRecord record)
    {
        // The display-name properties (LevelDisplayName, OpcodeDisplayName, TaskDisplayName,
        // KeywordsDisplayNames) resolve provider manifest metadata lazily and throw when that
        // metadata is missing on the host. Each read is isolated so a single unresolved event
        // degrades to its numeric/base fields instead of terminating collection.
        var fields = new Dictionary<string, object?>(16, StringComparer.OrdinalIgnoreCase)
        {
            ["ProviderName"] = record.ProviderName,
            ["EventId"] = record.Id,
            ["Qualifiers"] = record.Qualifiers,
            ["Channel"] = record.LogName,
            ["RecordId"] = record.RecordId,
            ["Level"] = SafeGet(() => record.LevelDisplayName) ?? record.Level?.ToString(),
            ["Opcode"] = SafeGet(() => record.OpcodeDisplayName) ?? record.Opcode?.ToString(),
            ["Task"] = SafeGet(() => record.TaskDisplayName) ?? record.Task?.ToString(),
            ["Keywords"] = SafeGet(() => record.KeywordsDisplayNames is null ? null : string.Join(",", record.KeywordsDisplayNames)),
            ["MachineName"] = record.MachineName,
            ["ProcessId"] = record.ProcessId,
            ["ThreadId"] = record.ThreadId,
            ["TimeCreated"] = record.TimeCreated,
            ["EventData"] = record.Properties.Select(property => property.Value).ToArray(),
            ["RawEvent"] = SafeGet(record.ToXml)
        };

        if (fields["RawEvent"] is string xml)
        {
            var namedEventData = ExtractNamedEventData(xml);
            if (namedEventData.Count > 0)
            {
                fields["EventData"] = namedEventData;
                foreach (var item in namedEventData)
                {
                    fields.TryAdd(item.Key, item.Value);
                }

                AddSecurityEventAliases(fields);
            }
        }

        var message = SafeGet(record.FormatDescription);
        if (!string.IsNullOrWhiteSpace(message))
        {
            fields["Message"] = message;
        }

        return fields;
    }


    private static void AddSecurityEventAliases(IDictionary<string, object?> fields)
    {
        if (!TryGetInt(fields.AsReadOnly(), "EventId", out var eventId) || eventId != 5156)
        {
            return;
        }

        AddFirstAlias(fields, "ApplicationPath", "Application", "ApplicationName", "Application_Name", "Application Name");
        AddFirstAlias(fields, "DestinationPort", "DestPort", "DestinationPort", "Destination_Port", "Destination Port");
    }

    private static void AddFirstAlias(IDictionary<string, object?> fields, string canonicalName, params string[] aliases)
    {
        if (fields.ContainsKey(canonicalName))
        {
            return;
        }

        foreach (var alias in aliases)
        {
            if (fields.TryGetValue(alias, out var value) && value is not null && !string.IsNullOrWhiteSpace(value.ToString()))
            {
                fields[canonicalName] = value;
                return;
            }
        }
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, object?> fields, string key, out int value)
    {
        if (fields.TryGetValue(key, out var raw))
        {
            switch (raw)
            {
                case int intValue:
                    value = intValue;
                    return true;
                case long longValue when longValue <= int.MaxValue && longValue >= int.MinValue:
                    value = (int)longValue;
                    return true;
                case string text when int.TryParse(text, out var parsed):
                    value = parsed;
                    return true;
            }
        }

        value = default;
        return false;
    }

    internal static IReadOnlyDictionary<string, object?> ExtractNamedEventData(string eventXml)
    {
        if (string.IsNullOrWhiteSpace(eventXml))
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var document = XDocument.Parse(eventXml, LoadOptions.None);
            var ns = document.Root?.Name.Namespace ?? XNamespace.None;
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var data in document.Descendants(ns + "EventData").Elements(ns + "Data"))
            {
                var name = data.Attribute("Name")?.Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                result[name] = data.Value;
            }

            foreach (var data in document.Descendants(ns + "UserData").Descendants())
            {
                if (!data.HasElements && !string.IsNullOrWhiteSpace(data.Name.LocalName))
                {
                    result.TryAdd(data.Name.LocalName, data.Value);
                }
            }

            return result;
        }
        catch (System.Xml.XmlException)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Evaluates a Windows Event Log property/formatting accessor, returning <see langword="null"/>
    /// when the underlying provider metadata cannot be resolved on this host. Isolates
    /// <see cref="EventLogException"/> and <see cref="Win32Exception"/> so a single unresolvable
    /// field does not terminate the collection loop.
    /// </summary>
    private static string? SafeGet(Func<string?> accessor)
    {
        try { return accessor(); }
        catch (EventLogException) { return null; }
        catch (Win32Exception) { return null; }
    }
}
