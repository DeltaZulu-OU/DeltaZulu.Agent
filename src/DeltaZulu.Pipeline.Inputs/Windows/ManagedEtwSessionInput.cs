using System.ComponentModel;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System.Reactive.Linq;
using System.Security.Principal;

namespace DeltaZulu.Pipeline.Inputs.Windows;

public sealed class ManagedEtwSessionInput : ISourceInput
{
    private readonly string _sessionName;
    private readonly string _providerName;
    private readonly Action<string>? _warn;
    public string Name { get; }

    public ManagedEtwSessionInput(string sessionName, string providerName, string? name = null, Action<string>? warn = null)
    {
        _sessionName = sessionName;
        _providerName = providerName;
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
            session => TraceEventSessionObservable.FromSession(session, Name, nameof(ManagedEtwSessionInput), _warn));
    }

    private TraceEventSession CreateSession()
    {
        try
        {
            return CreateAndEnableSession();
        }
        catch (Exception ex) when (IsSessionAlreadyExists(ex))
        {
            _warn?.Invoke($"Managed ETW session '{_sessionName}': existing session found, attaching without stopping it.");
            return AttachAndEnableSession();
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
            StopOnDispose = true
        };

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

    private TraceEventSession AttachAndEnableSession()
    {
        var session = new TraceEventSession(_sessionName, TraceEventSessionOptions.Attach)
        {
            StopOnDispose = false
        };

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

    private static bool IsSessionLimitExceeded(Exception ex) =>
        GetWin32ErrorCode(ex) == ErrorNoSystemResources;

    private static bool IsSessionAlreadyExists(Exception ex) =>
        GetWin32ErrorCode(ex) == ErrorAlreadyExists;

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

        session.EnableProvider(_providerName);
    }
}
