using CsvHelper;
using DeltaZulu.Agent.Shared.Pipeline.Abstractions;
using DeltaZulu.Agent.Shared.Pipeline.Events;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace DeltaZulu.Agent.Inputs.Files;

public sealed class CsvFileInput : ISourceInput
{
    private readonly string _path;
    public string Name { get; }

    public CsvFileInput(string path, string name = "csv-file")
    {
        _path = path;
        Name = name;
    }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default) => Observable.Create<SourceEvent>(observer => {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ReadAsync(observer, cts.Token), cts.Token);
        return Disposable.Create(() => cts.Cancel());
    });

    private async Task ReadAsync(IObserver<SourceEvent> observer, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            await csv.ReadAsync().ConfigureAwait(false);
            csv.ReadHeader();
            var headers = csv.HeaderRecord ?? [];
            while (await csv.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var header in headers)
                {
                    fields[header] = Coerce(csv.GetField(header));
                }

                var metadata = new ResourceMetadata
                {
                    SourceType = "Csv",
                    SourceName = Name,
                    Platform = "portable",
                    Hostname = Environment.MachineName,
                    ParserName = nameof(CsvFileInput),
                    RawPreserved = false
                };

                observer.OnNext(new SourceEvent(metadata, fields));
            }

            observer.OnCompleted();
        }
        catch (OperationCanceledException)
        {
            observer.OnCompleted();
        }
        catch (Exception ex)
        {
            observer.OnError(ex);
        }
    }

    private static object? Coerce(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, out var dto))
        {
            return dto;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
        {
            return l;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }

        if (bool.TryParse(value, out var b))
        {
            return b;
        }

        return value;
    }
}