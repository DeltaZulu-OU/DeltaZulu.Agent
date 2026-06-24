using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using System.Reactive.Linq;

namespace DeltaZulu.Agent.Inputs.Windows;

public sealed class WindowsEventLogInput : IResourceInput
{
    private readonly string _logName;
    public string Name { get; }

    public WindowsEventLogInput(string logName, string? name = null)
    {
        _logName = logName;
        Name = name ?? logName;
    }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default)
    {
        return Tx.Windows.EvtxObservable.FromLog(_logName, null, false)
            .Select(x => WindowsSourceEventMapper.FromDictionary(x.AsDictionary().AsReadOnly(), "WindowsEventLog", Name, nameof(WindowsEventLogInput)));
    }
}