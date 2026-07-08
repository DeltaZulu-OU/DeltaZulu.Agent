using System.ComponentModel;
using System.Reactive.Linq;
using System.Security.Principal;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;
using DeltaZulu.Pipeline.Inputs.Etw;
using Microsoft.Diagnostics.Tracing.Session;

namespace DeltaZulu.Pipeline.Inputs.Windows;

public sealed class EtwSessionInput : ISourceInput
{
    private readonly string _sessionName;
    private readonly NativeEtwIdentityFilter? _nativeFilter;
    private readonly IReadOnlySet<string>? _selectedPayloadFields;
    private readonly EtwCollectorMetrics? _metrics;
    private readonly Action<string>? _warn;
    public string Name { get; }

    public EtwSessionInput(string sessionName, string? name = null, Action<string>? warn = null)
        : this(sessionName, resource: null, name: name, metrics: null, warn: warn)
    {
    }

    public EtwSessionInput(string sessionName, ResourceDescriptor? resource, string? name = null, EtwCollectorMetrics? metrics = null, Action<string>? warn = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionName);
        _sessionName = sessionName;


        _nativeFilter = resource is null ? null : EtwNativeFilterCompiler.Compile(resource);
        _selectedPayloadFields = EtwPayloadProjection.BuildSelectedPayloadFields(resource);
        _metrics = metrics;
        _warn = warn;
        Name = name ?? $"etw.{SanitizeName(sessionName)}";
    }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_sessionName))
        {
            return Observable.Throw<SourceEvent>(new InvalidOperationException("Attach-mode ETW input requires resource.session to name an existing ETW session."));
        }

        var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            return Observable.Throw<SourceEvent>(new UnauthorizedAccessException("Administrator privileges are required to attach to a real-time ETW session."));
        }

        return Observable.Defer(() => {
            try
            {
                var session = new TraceEventSession(_sessionName, TraceEventSessionOptions.Attach);
                return Observable.Using(
                    () => session,
                    attached => TraceEventSessionObservable.FromSession(attached, Name, nameof(EtwSessionInput), _nativeFilter, _selectedPayloadFields, _metrics, _warn));
            }
            catch (Exception ex) when (IsSessionOpenException(ex))
            {
                return Observable.Throw<SourceEvent>(CreateSessionOpenException(ex));
            }
            catch (ArgumentException ex)
            {
                return Observable.Throw<SourceEvent>(CreateInvalidSessionException(ex));
            }
        }).Catch<SourceEvent, Exception>(ex => IsSessionOpenException(ex)
            ? Observable.Throw<SourceEvent>(CreateSessionOpenException(ex))
            : Observable.Throw<SourceEvent>(ex));
    }

    private const int ErrorInvalidParameter = 87;
    private const int ErrorNotFound = 1168;
    private const int ErrorNoSystemResources = 1450;

    private static bool IsSessionOpenException(Exception exception)
    {
        var errorCode = GetWin32ErrorCode(exception);
        return exception is Win32Exception
            || exception is ArgumentException
            || errorCode is ErrorInvalidParameter or ErrorNotFound or ErrorNoSystemResources;
    }

    private InvalidOperationException CreateInvalidSessionException(ArgumentException innerException) => new(
        $"ETW session '{_sessionName}' could not be opened in attach mode because TraceEvent rejected the session name or attach options. " +
        $"Attach-mode ETW profiles require resource.session to name an already running ETW session. Managed ETW profiles must use resource.mode: managed and resource.provider so DeltaZulu can create its own session.",
        innerException);

    private InvalidOperationException CreateSessionOpenException(Exception innerException)
    {
        var errorCode = GetWin32ErrorCode(innerException);
        if (errorCode == ErrorNoSystemResources)
        {
            return new InvalidOperationException(
                $"ETW session '{_sessionName}' could not be opened: the OS ETW session limit has been reached (default 64, max 256). " +
                "Stop unused sessions or increase the limit via HKLM\\SYSTEM\\CurrentControlSet\\Control\\WMI\\EtwMaxLoggers.",
                innerException);
        }

        if (errorCode is ErrorInvalidParameter or ErrorNotFound || innerException is ArgumentException)
        {
            return new InvalidOperationException(
                $"ETW session '{_sessionName}' could not be attached. Attach-mode ETW input requires resource.session to name an already running ETW session. " +
                $"For DeltaZulu-owned provider sessions use resource.mode: managed with resource.session and resource.provider. {FormatActiveSessionsHint()}",
                innerException);
        }

        return new InvalidOperationException(
            $"ETW session '{_sessionName}' could not be opened. DeltaZulu attach-mode ETW input is attach-only; create and enable the session before starting the profile, or use managed ETW mode when DeltaZulu should create the session.",
            innerException);
    }

    private static int? GetWin32ErrorCode(Exception exception)
    {
        if (exception is Win32Exception win32Exception)
        {
            return win32Exception.NativeErrorCode;
        }

        if (exception.InnerException is Win32Exception innerWin32Exception)
        {
            return innerWin32Exception.NativeErrorCode;
        }

        return exception.HResult == 0 ? null : exception.HResult & 0xFFFF;
    }

    private static string FormatActiveSessionsHint()
    {
        try
        {
            var activeSessions = TraceEventSession.GetActiveSessionNames()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToArray();

            return activeSessions.Length == 0
                ? "No active ETW sessions were visible to this process."
                : $"Visible active sessions: {string.Join(", ", activeSessions)}.";
        }
        catch
        {
            return "Active ETW sessions could not be enumerated.";
        }
    }

    private static string SanitizeName(string value) => string.IsNullOrWhiteSpace(value)
        ? "attach"
        : value.Trim().Replace(' ', '.');
}
