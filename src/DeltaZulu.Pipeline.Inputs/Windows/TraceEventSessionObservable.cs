using DeltaZulu.Pipeline.Core.Events;
using Microsoft.Diagnostics.Tracing.Session;
using System.Reactive.Disposables;

namespace DeltaZulu.Pipeline.Inputs.Windows;

internal static class TraceEventSessionObservable
{
    public static IObservable<SourceEvent> FromSession(
        TraceEventSession session,
        string sourceName,
        string sourceInput,
        Action<string>? warn = null)
    {
        return System.Reactive.Linq.Observable.Create<SourceEvent>(observer =>
        {
            var disposed = 0;

            session.Source.Dynamic.All += data =>
            {
                try
                {
                    var fields = TraceEventSourceEventMapper.ToDictionary(data);
                    observer.OnNext(WindowsSourceEventMapper.FromDictionary(fields, "WindowsEtw", sourceName, sourceInput));
                }
                catch (Exception ex)
                {
                    warn?.Invoke($"ETW source '{sourceName}': event skipped due to TraceEvent materialization error: {ex.Message}");
                }
            };

            var processing = Task.Run(() =>
            {
                try
                {
                    session.Source.Process();
                    if (Volatile.Read(ref disposed) == 0)
                    {
                        observer.OnCompleted();
                    }
                }
                catch (ObjectDisposedException) when (Volatile.Read(ref disposed) != 0)
                {
                }
                catch (Exception ex) when (Volatile.Read(ref disposed) != 0 && IsExpectedShutdownException(ex))
                {
                }
                catch (Exception ex)
                {
                    observer.OnError(ex);
                }
            });

            return Disposable.Create(() =>
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0)
                {
                    return;
                }

                try
                {
                    session.Source.StopProcessing();
                }
                catch
                {
                }

                try
                {
                    processing.Wait(TimeSpan.FromSeconds(5));
                }
                catch
                {
                }
            });
        });
    }

    private static bool IsExpectedShutdownException(Exception ex) =>
        ex is InvalidOperationException || ex is ObjectDisposedException;
}
