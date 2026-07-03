using System.Net.Sockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Inputs.Common;

/// <summary>
/// Shared accept-loop plumbing for TCP-listener-based inputs (syslog, RELP): starts a listener,
/// spawns one handler per accepted client, and tears both down on unsubscribe.
/// </summary>
internal static class TcpListenerSourceInput
{
    public static IObservable<SourceEvent> Create(
        Func<TcpListener> createListener,
        Func<TcpClient, IObserver<SourceEvent>, CancellationToken, Task> handleClientAsync,
        CancellationToken cancellationToken) =>
        Observable.Create<SourceEvent>(observer => {
            var listener = createListener();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _ = Task.Run(async () => {
                try
                {
                    while (!linkedCts.IsCancellationRequested)
                    {
                        var client = await listener.AcceptTcpClientAsync(linkedCts.Token).ConfigureAwait(false);
                        _ = Task.Run(() => handleClientAsync(client, observer, linkedCts.Token), linkedCts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    observer.OnCompleted();
                }
                catch (Exception ex)
                {
                    observer.OnError(ex);
                }
            }, linkedCts.Token);

            return Disposable.Create(() => {
                linkedCts.Cancel();
                listener.Stop();
                linkedCts.Dispose();
            });
        })
        // Multiple client-handler tasks call the same observer concurrently; Rx requires
        // serialized notifications, so synchronize them here rather than in each caller.
        .Synchronize();
}
