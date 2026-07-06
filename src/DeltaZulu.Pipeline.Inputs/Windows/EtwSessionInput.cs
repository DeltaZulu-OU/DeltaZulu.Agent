using System.ComponentModel;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Inputs.Etw;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;
using Microsoft.Diagnostics.Tracing.Session;
using System.Reactive.Linq;
using System.Security.Principal;

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
        _sessionName = sessionName;
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
            return Observable.Throw<SourceEvent>(new UnauthorizedAccessException("Administrator privileges are required to attach to a real-time ETW session."));
        }

        return Observable.Defer(() =>
        {
            try
            {
                var session = new TraceEventSession(_sessionName, TraceEventSessionOptions.Attach);
                return Observable.Using(
                    () => session,
                    attached => TraceEventSessionObservable.FromSession(attached, Name, nameof(EtwSessionInput), _nativeFilter, _selectedPayloadFields, _metrics, _warn));
            }
            catch (Win32Exception ex)
            {
                return Observable.Throw<SourceEvent>(CreateSessionOpenException(ex));
            }
        }).Catch<SourceEvent, Win32Exception>(ex => Observable.Throw<SourceEvent>(CreateSessionOpenException(ex)));
    }

    private const int ErrorNoSystemResources = 1450;

    private InvalidOperationException CreateSessionOpenException(Win32Exception innerException)
    {
        if (innerException.NativeErrorCode == ErrorNoSystemResources)
        {
            return new InvalidOperationException(
                $"ETW session '{_sessionName}' could not be opened: the OS ETW session limit has been reached (default 64, max 256). " +
                $"Stop unused sessions or increase the limit via HKLM\\SYSTEM\\CurrentControlSet\\Control\\WMI\\EtwMaxLoggers.",
                innerException);
        }

        return new InvalidOperationException(
            $"ETW session '{_sessionName}' could not be opened. DeltaZulu ETW input is attach-only; create and enable the session before starting the profile, or mark the profile non-mandatory if the session is optional.",
            innerException);
    }
}
