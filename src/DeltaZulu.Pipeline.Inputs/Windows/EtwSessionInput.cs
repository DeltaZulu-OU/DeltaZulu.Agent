using System.ComponentModel;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using System.Reactive.Linq;
using System.Security.Principal;

namespace DeltaZulu.Pipeline.Inputs.Windows;

public sealed class EtwSessionInput : ISourceInput
{
    private readonly string _sessionName;
    public string Name { get; }

    public EtwSessionInput(string sessionName, string? name = null)
    {
        _sessionName = sessionName;
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
                return Tx.Windows.EtwTdhObservable.FromSession(_sessionName)
                    .Select(x =>
                    {
                        var fields = EtwTdhEventFields.Materialize(x);
                        return WindowsSourceEventMapper.FromDictionary(fields, "WindowsEtw", Name, nameof(EtwSessionInput));
                    });
            }
            catch (Win32Exception ex)
            {
                return Observable.Throw<SourceEvent>(CreateSessionOpenException(ex));
            }
        }).Catch<SourceEvent, Win32Exception>(ex => Observable.Throw<SourceEvent>(CreateSessionOpenException(ex)));
    }

    private InvalidOperationException CreateSessionOpenException(Win32Exception innerException) => new(
        $"ETW session '{_sessionName}' could not be opened. DeltaZulu ETW input is attach-only; create and enable the session before starting the profile, or mark the profile non-mandatory if the session is optional.",
        innerException);
}
