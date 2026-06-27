using DeltaZulu.Agent.Pipeline.Abstractions;
using DeltaZulu.Agent.Pipeline.Events;
using System.Reactive.Linq;
using System.Security.Principal;

namespace DeltaZulu.Agent.Inputs.Windows;

public sealed class EtwSessionInput : ISourceInput
{
    private readonly string _sessionName;
    public string Name { get; }

    public EtwSessionInput(string sessionName, string? name = null)
    {
        _sessionName = sessionName;
        Name = name ?? sessionName;
    }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default)
    {
        var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            return Observable.Throw<SourceEvent>(new UnauthorizedAccessException("Administrator privileges are required to attach to a real-time ETW session."));
        }

        return Tx.Windows.EtwTdhObservable.FromSession(_sessionName)
            .Select(x => {
                var fields = x.AsDictionary().AsReadOnly();
                var sourceName = fields.TryGetValue("ProviderName", out var providerName) && providerName is not null
                    ? providerName.ToString() ?? Name
                    : Name;
                return WindowsSourceEventMapper.FromDictionary(fields, "WindowsEtw", sourceName, nameof(EtwSessionInput));
            });
    }
}