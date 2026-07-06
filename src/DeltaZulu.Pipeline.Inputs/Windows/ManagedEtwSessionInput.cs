using System.ComponentModel;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Inputs.Etw;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System.Reflection;
using System.Reactive.Linq;
using System.Security.Principal;

namespace DeltaZulu.Pipeline.Inputs.Windows;

public sealed class ManagedEtwSessionInput : ISourceInput
{
    // Managed ETW sessions need enough room for bursty Security/DNS events while
    // keeping orphaned sessions bounded if the process exits abruptly. Use a
    // 256 KB ETW buffer quantum for large PowerShell/Security payloads and a
    // 64 MB pool budget, which yields 256 maximum buffers.
    internal const int ManagedSessionBufferSizeKilobytes = 256;
    internal const int ManagedSessionMemoryBudgetMegabytes = 64;
    internal const int ManagedSessionMaximumBuffers = ManagedSessionMemoryBudgetMegabytes * 1024 / ManagedSessionBufferSizeKilobytes;
    internal const int ManagedSessionMinimumBuffers = ManagedSessionMaximumBuffers / 4;

    private const string TraceEventBufferQuantumFieldName = "m_BufferQuantumKB";

    private readonly string _sessionName;
    private readonly string _providerName;
    private readonly ResourceDescriptor? _resource;
    private readonly NativeEtwIdentityFilter? _nativeFilter;
    private readonly IReadOnlySet<string>? _selectedPayloadFields;
    private readonly EtwCollectorMetrics? _metrics;
    private readonly Action<string>? _warn;
    public string Name { get; }

    public ManagedEtwSessionInput(string sessionName, string providerName, string? name = null, Action<string>? warn = null)
        : this(sessionName, providerName, resource: null, name: name, metrics: null, warn: warn)
    {
    }

    public ManagedEtwSessionInput(string sessionName, string providerName, ResourceDescriptor? resource, string? name = null, EtwCollectorMetrics? metrics = null, Action<string>? warn = null)
    {
        _sessionName = sessionName;
        _providerName = providerName;
        _resource = resource;
        _nativeFilter = resource is null ? null : EtwNativeFilterCompiler.Compile(resource);
        _selectedPayloadFields = EtwPayloadProjection.BuildSelectedPayloadFields(resource);
        _metrics = metrics;
        _warn = warn;
        Name = name ?? $"etw{sessionName}";
    }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default)
    {
        var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            return Observable.Throw<SourceEvent>(new UnauthorizedAccessException("Administrator privileges are required to create and attach to a real-time ETW session."));
        }

        if (string.IsNullOrWhiteSpace(_providerName))
        {
            return Observable.Throw<SourceEvent>(new InvalidOperationException($"Managed ETW session '{_sessionName}' requires resource.provider."));
        }

        return Observable.Using(
            CreateSession,
            session => TraceEventSessionObservable.FromSession(session, Name, nameof(ManagedEtwSessionInput), _nativeFilter, _selectedPayloadFields, _metrics, _warn));
    }

    private TraceEventSession CreateSession()
    {
        try
        {
            return CreateAndEnableSession();
        }
        catch (Exception ex) when (IsSessionAlreadyExists(ex))
        {
            _warn?.Invoke($"Managed ETW session '{_sessionName}': existing session found; reclaiming it before creating a fresh DeltaZulu-owned session.");
            StopExistingSession();
            return CreateAndEnableSession();
        }
        catch (Exception ex) when (IsSessionLimitExceeded(ex))
        {
            var activeCount = GetActiveSessionCount();
            throw new InvalidOperationException(
                $"ETW session limit reached ({activeCount} active sessions). " +
                $"The OS allows 64 concurrent ETW sessions by default (max 256 via registry). " +
                $"Stop unused sessions or increase the limit via HKLM\\SYSTEM\\CurrentControlSet\\Control\\WMI\\EtwMaxLoggers.",
                ex);
        }
    }

    private TraceEventSession CreateAndEnableSession()
    {
        var session = new TraceEventSession(_sessionName)
        {
            StopOnDispose = true,
            BufferSizeMB = ManagedSessionMemoryBudgetMegabytes
        };
        ConfigureBufferSize(session);

        try
        {
            EnableProvider(session);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }


    private static void ConfigureBufferSize(TraceEventSession session)
    {
        // TraceEvent exposes the total ETW buffer pool as BufferSizeMB, but not
        // EVENT_TRACE_PROPERTIES.BufferSize. The field below is TraceEvent's
        // backing buffer quantum, set before EnableProvider starts the session.
        var field = typeof(TraceEventSession).GetField(
            TraceEventBufferQuantumFieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        field?.SetValue(session, ManagedSessionBufferSizeKilobytes);
    }

    private void StopExistingSession()
    {
        try
        {
            using var session = new TraceEventSession(_sessionName, TraceEventSessionOptions.Attach)
            {
                StopOnDispose = true
            };
        }
        catch (Exception stopException) when (IsSessionNotFound(stopException))
        {
            _warn?.Invoke($"Managed ETW session '{_sessionName}': existing session disappeared before it could be reclaimed.");
        }
        catch (Exception stopException)
        {
            throw new InvalidOperationException($"Managed ETW session '{_sessionName}' already exists and could not be reclaimed.", stopException);
        }
    }

    private static int GetActiveSessionCount()
    {
        try
        {
            return TraceEventSession.GetActiveSessionNames().Count;
        }
        catch
        {
            return -1;
        }
    }

    private const int ErrorNoSystemResources = 1450;
    private const int ErrorAlreadyExists = 183;
    private const int ErrorNotFound = 1168;

    private static bool IsSessionLimitExceeded(Exception ex) =>
        GetWin32ErrorCode(ex) == ErrorNoSystemResources;

    private static bool IsSessionAlreadyExists(Exception ex) =>
        GetWin32ErrorCode(ex) == ErrorAlreadyExists;

    private static bool IsSessionNotFound(Exception ex) =>
        GetWin32ErrorCode(ex) == ErrorNotFound;

    private static int? GetWin32ErrorCode(Exception ex)
    {
        if (ex is Win32Exception w32)
            return w32.NativeErrorCode;
        if (ex.InnerException is Win32Exception inner)
            return inner.NativeErrorCode;
        return ex.HResult == 0 ? null : ex.HResult & 0xFFFF;
    }

    private void EnableProvider(TraceEventSession session)
    {
        if (_providerName.Equals("Microsoft-Windows-Kernel-Process", StringComparison.OrdinalIgnoreCase))
        {
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);
            return;
        }

        session.EnableProvider(_providerName, TraceEventLevel.Verbose, ulong.MaxValue, BuildProviderOptions(_resource));
    }

    private static readonly EtwResourceOptionsAdapter OptionsAdapter = new();

    internal static TraceEventProviderOptions? BuildProviderOptions(ResourceDescriptor? resource)
    {
        if (resource is null)
        {
            return null;
        }

        var etwOptions = OptionsAdapter.Adapt(resource);

        var options = new TraceEventProviderOptions
        {
            StacksEnabled = etwOptions.CaptureStacks,
            EnableInContainers = etwOptions.EnableInContainers,
            EnableSourceContainerTracking = etwOptions.EnableSourceContainerTracking
        };

        if (etwOptions.ProcessIds.Count > 0)
        {
            options.ProcessIDFilter = etwOptions.ProcessIds;
        }

        if (etwOptions.ProcessNames.Count > 0)
        {
            options.ProcessNameFilter = etwOptions.ProcessNames;
        }

        if (etwOptions.EventIds.Count > 0)
        {
            options.EventIDsToEnable = etwOptions.EventIds;
        }

        if (etwOptions.ExcludedEventIds.Count > 0)
        {
            options.EventIDsToDisable = etwOptions.ExcludedEventIds;
        }

        if (etwOptions.StackEventIds.Count > 0)
        {
            options.EventIDStacksToEnable = etwOptions.StackEventIds;
        }

        if (etwOptions.ExcludedStackEventIds.Count > 0)
        {
            options.EventIDStacksToDisable = etwOptions.ExcludedStackEventIds;
        }

        return HasProviderOptions(etwOptions) ? options : null;
    }

    private static bool HasProviderOptions(EtwResourceOptions options) =>
        options.CaptureStacks ||
        options.EnableInContainers ||
        options.EnableSourceContainerTracking ||
        options.ProcessIds.Count > 0 ||
        options.ProcessNames.Count > 0 ||
        options.EventIds.Count > 0 ||
        options.ExcludedEventIds.Count > 0 ||
        options.StackEventIds.Count > 0 ||
        options.ExcludedStackEventIds.Count > 0;
}
