using DeltaZulu.Agent.Application.Abstractions;
using DeltaZulu.Agent.Core.Events;
using System.Reactive.Linq;

namespace DeltaZulu.Agent.Inputs.Windows;

public sealed class EtlFileInput : ISourceInput
{
    private readonly string _path;
    public string Name { get; }

    public EtlFileInput(string path, string name = "etl-file")
    {
        _path = path;
        Name = name;
    }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return Observable.Throw<SourceEvent>(new FileNotFoundException("ETL file does not exist.", _path));
        }

        return Tx.Windows.EtwTdhObservable.FromFiles(_path)
            .Select(x => WindowsSourceEventMapper.FromDictionary(x.AsDictionary().AsReadOnly(), "WindowsEtw", Name, nameof(EtlFileInput)));
    }
}