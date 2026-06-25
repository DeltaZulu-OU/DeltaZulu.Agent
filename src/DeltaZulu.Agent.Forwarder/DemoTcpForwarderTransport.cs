using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DeltaZulu.Agent.Forwarder;

public sealed class DemoTcpForwarderTransport : IForwarderTransport
{
    private readonly string _host;
    private readonly int _port;

    public DemoTcpForwarderTransport(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public async ValueTask<DeliveryAck> SendAsync(
        DeliveryBatch batch,
        CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_host, _port, cancellationToken);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };

        var json = JsonSerializer.Serialize(batch, ForwarderJson.Options);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken);

        var ackLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(ackLine))
        {
            return new DeliveryAck
            {
                BatchId = batch.BatchId,
                Accepted = false,
                Reason = "Demo forwarder server closed the connection without an ACK."
            };
        }

        var ack = JsonSerializer.Deserialize<DeliveryAck>(ackLine, ForwarderJson.Options);
        return ack ?? new DeliveryAck
        {
            BatchId = batch.BatchId,
            Accepted = false,
            Reason = "Demo forwarder server returned an invalid ACK."
        };
    }
}
