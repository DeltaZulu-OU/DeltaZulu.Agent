using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using System.Reactive.Linq;

namespace DeltaZulu.Pipeline.Inputs.Windows;

public sealed class EtlFileInput : ISourceInput
{
    private readonly string _path;
    private readonly Action<string>? _warn;
    private readonly EtwTdhDropWarningLimiter _dropWarningLimiter;
    public string Name { get; }

    public EtlFileInput(string path, string name = "etl-file", Action<string>? warn = null)
    {
        _path = path;
        _warn = warn;
        _dropWarningLimiter = new EtwTdhDropWarningLimiter(warn, $"ETL file '{path}'");
        Name = name;
    }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return Observable.Throw<SourceEvent>(new FileNotFoundException("ETL file does not exist.", _path));
        }

        return Tx.Windows.EtwTdhObservable.FromFiles(_path)
            .SelectMany(x => MaterializeEvent(x));
    }

    private IObservable<SourceEvent> MaterializeEvent(IDictionary<string, object> raw)
    {
        try
        {
            if (!EtwTdhEventFields.TryMaterialize(raw, out var fields, OnEventDropped))
            {
                return Observable.Empty<SourceEvent>();
            }

            return Observable.Return(WindowsSourceEventMapper.FromDictionary(fields, "WindowsEtw", Name, nameof(EtlFileInput)));
        }
        catch (Exception ex)
        {
            _warn?.Invoke($"ETL file '{_path}': event skipped due to unexpected materialization error: {ex.Message}");
            return Observable.Empty<SourceEvent>();
        }
    }

    private void OnEventDropped(Exception ex)
    {
        _dropWarningLimiter.OnEventDropped(ex);
    }
}
