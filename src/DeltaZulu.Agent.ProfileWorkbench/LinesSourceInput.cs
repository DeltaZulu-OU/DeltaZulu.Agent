using System.Reactive.Linq;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Agent.ProfileWorkbench;

internal sealed class LinesSourceInput : ISourceInput
{
    private readonly string _path;
    private readonly bool _follow;

    public LinesSourceInput(string path, bool follow)
    {
        _path = path;
        _follow = follow;
    }

    public string Name => "lines-file";

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default) => Observable.Create<SourceEvent>(observer => {
        return Task.Run(async () => {
            try
            {
                var lineNumber = 0L;
                var position = 0L;
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!File.Exists(_path))
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (position > stream.Length)
                    {
                        position = 0;
                    }

                    stream.Seek(position, SeekOrigin.Begin);
                    using var reader = new StreamReader(stream, leaveOpen: true);
                    while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                        if (line is null)
                        {
                            break;
                        }

                        lineNumber++;
                        observer.OnNext(new SourceEvent(
                            new ResourceMetadata
                            {
                                SourceType = "file",
                                SourceName = _path,
                                Platform = "local",
                                ParserName = "dzagentctl.lines",
                                RawPreserved = true,
                                Properties = new Dictionary<string, object?> { ["lineNumber"] = lineNumber }
                            },
                            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["lineNumber"] = lineNumber,
                                ["line"] = line
                            }));
                    }

                    position = stream.Position;
                    if (!_follow)
                    {
                        observer.OnCompleted();
                        return;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                }

                observer.OnCompleted();
            }
            catch (OperationCanceledException)
            {
                observer.OnCompleted();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                observer.OnError(ex);
            }
        }, cancellationToken);
    });
}
