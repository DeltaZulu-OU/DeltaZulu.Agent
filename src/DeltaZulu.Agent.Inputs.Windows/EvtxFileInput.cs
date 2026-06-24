using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using System.Reactive.Linq;

namespace DeltaZulu.Agent.Inputs.Windows;

public sealed class EvtxFileInput : IResourceInput
{
    private readonly string _path;
    public string Name { get; }

    public EvtxFileInput(string path, string name = "evtx-file")
    {
        _path = path;
        Name = name;
    }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return Observable.Throw<SourceEvent>(new FileNotFoundException("EVTX file does not exist.", _path));
        }

        return Tx.Windows.EvtxObservable.FromFiles(_path)
            .Select(x => WindowsSourceEventMapper.FromDictionary(x.AsDictionary().AsReadOnly(), "WindowsEventLog", Name, nameof(EvtxFileInput)));
    }
}