using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;

namespace DeltaZulu.Agent.Inputs.Syslog;

public sealed class TcpSyslogInput : IResourceInput
{
    private readonly IPAddress _address;
    private readonly int _port;
    private readonly LightweightSyslogParser _parser;

    public string Name { get; }

    public TcpSyslogInput(IPAddress address, int port, string name = "tcp-syslog")
    {
        _address = address;
        _port = port;
        Name = name;
        _parser = new LightweightSyslogParser();
    }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default) => Observable.Create<SourceEvent>(observer => {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var listener = new TcpListener(_address, _port);
        listener.Start();

        _ = Task.Run(async () => {
            try
            {
                while (!linkedCts.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(linkedCts.Token).ConfigureAwait(false);
                    _ = Task.Run(() => ReadClientAsync(client, observer, linkedCts.Token), linkedCts.Token);
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
    });

    private async Task ReadClientAsync(TcpClient client, IObserver<SourceEvent> observer, CancellationToken cancellationToken)
    {
        using var _ = client;
        using var reader = new StreamReader(client.GetStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                continue;
            }

            observer.OnNext(_parser.Parse(line, Name, client.Client.RemoteEndPoint?.ToString()));
        }
    }
}