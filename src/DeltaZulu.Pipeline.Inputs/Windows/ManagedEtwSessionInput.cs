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
    public string Name { get; }

    public ManagedEtwSessionInput(string sessionName, string providerName, string? name = null)
    {
        _sessionName = sessionName;
        _providerName = providerName;
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
            _ => Tx.Windows.EtwTdhObservable.FromSession(_sessionName)
                .Select(x =>
                {
                    var fields = EtwTdhEventFields.Materialize(x);
                    return WindowsSourceEventMapper.FromDictionary(fields, "WindowsEtw", Name, nameof(ManagedEtwSessionInput));
                }));
    }

    private IDisposable CreateSession()
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
