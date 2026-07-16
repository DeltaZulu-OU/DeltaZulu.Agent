using System.Net;
using System.Net.Sockets;
using System.Text;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Inputs.Common;

namespace DeltaZulu.Pipeline.Inputs.Syslog;

public sealed class TcpSyslogInput : ISourceInput
{
    private readonly IPAddress _address;
    private readonly LightweightSyslogParser _parser;
    private readonly int _port;

    public TcpSyslogInput(IPAddress address, int port, string name = "tcp-syslog")
    {
        _address = address;
        _port = port;
        Name = name;
        _parser = new LightweightSyslogParser();
    }

    public string Name { get; }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default) =>
        TcpListenerSourceInput.Create(
            createListener: () => {
                var listener = new TcpListener(_address, _port);
                listener.Start();
                return listener;
            },
            handleClientAsync: ReadClientAsync,
            cancellationToken);

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
