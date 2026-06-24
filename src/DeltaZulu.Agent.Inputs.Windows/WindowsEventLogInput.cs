using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using System.Diagnostics.Eventing.Reader;
using System.Reactive.Disposables;

namespace DeltaZulu.Agent.Inputs.Windows;

public sealed class WindowsEventLogInput : IResourceInput
{
    private readonly string _requestedLogName;
    private readonly TimeSpan _pollInterval;
    private string? _resolvedLogName;
    public string Name { get; }

    public WindowsEventLogInput(string logName, string? name = null, TimeSpan? pollInterval = null)
    {
        _requestedLogName = logName;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        Name = name ?? logName;
    }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default)
    {
        return System.Reactive.Linq.Observable.Create<SourceEvent>(observer =>
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = Task.Run(() => PollAsync(observer, cts.Token), cts.Token);
            return Disposable.Create(() => cts.Cancel());
        });
    }

    private async Task PollAsync(IObserver<SourceEvent> observer, CancellationToken cancellationToken)
    {
        try
        {
            if (!TryResolveLogName(out var logName, out var errorMessage))
            {
                observer.OnCompleted();
                return;
            }

            var lastRecordId = ReadLatestRecordId(logName);
            while (!cancellationToken.IsCancellationRequested)
            {
                lastRecordId = ReadNewEvents(observer, logName, lastRecordId);
                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            }

            observer.OnCompleted();
        }
        catch (OperationCanceledException)
        {
            observer.OnCompleted();
        }
        catch (Exception ex)
        {
            observer.OnError(ex);
        }
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

                observer.OnNext(WindowsSourceEventMapper.FromDictionary(ToDictionary(record), "WindowsEventLog", Name, nameof(WindowsEventLogInput)));
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

    private static string ExpandLogAlias(string logName) => logName.ToLowerInvariant() switch
    {
        "sysmon" => "Microsoft-Windows-Sysmon/Operational",
        "sysmon-operational" => "Microsoft-Windows-Sysmon/Operational",
        "security" => "Security",
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
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProviderName"] = record.ProviderName,
            ["EventId"] = record.Id,
            ["Qualifiers"] = record.Qualifiers,
            ["Channel"] = record.LogName,
            ["RecordId"] = record.RecordId,
            ["Level"] = record.LevelDisplayName ?? record.Level?.ToString(),
            ["Opcode"] = record.OpcodeDisplayName ?? record.Opcode?.ToString(),
            ["Task"] = record.TaskDisplayName ?? record.Task?.ToString(),
            ["Keywords"] = record.KeywordsDisplayNames is null ? null : string.Join(",", record.KeywordsDisplayNames),
            ["MachineName"] = record.MachineName,
            ["ProcessId"] = record.ProcessId,
            ["ThreadId"] = record.ThreadId,
            ["TimeCreated"] = record.TimeCreated,
            ["EventData"] = record.Properties.Select(property => property.Value).ToArray(),
            ["RawEvent"] = SafeFormat(record.ToXml)
        };

        var message = SafeFormat(record.FormatDescription);
        if (!string.IsNullOrWhiteSpace(message))
        {
            fields["Message"] = message;
        }

        return fields;
    }

    private static string? SafeFormat(Func<string> formatter)
    {
        try { return formatter(); }
        catch (EventLogException) { return null; }
    }
}
