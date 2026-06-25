using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DeltaZulu.Agent.Forwarder;

public sealed class DemoForwarderServer
{
    private readonly IPAddress _address;
    private readonly int _port;
    private readonly TextWriter _output;

    public DemoForwarderServer(IPAddress address, int port, TextWriter? output = null)
    {
        _address = address;
        _port = port;
        _output = output ?? Console.Out;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var listener = new TcpListener(_address, _port);
        listener.Start();
        await _output.WriteLineAsync($"DeltaZulu demo forwarder server listening on {_address}:{_port}");
        await _output.FlushAsync(cancellationToken);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var ownedClient = client;
        await using var stream = ownedClient.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };

        var line = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var batch = JsonSerializer.Deserialize<DeliveryBatch>(line, ForwarderJson.Options);
        if (batch is null)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(new DeliveryAck
            {
                BatchId = "unknown",
                Accepted = false,
                Reason = "Invalid batch JSON."
            }, ForwarderJson.Options).AsMemory(), cancellationToken);
            return;
        }

        await _output.WriteLineAsync($"batch {batch.BatchId} records={batch.Records.Count} createdAt={batch.CreatedAt:O}");
        foreach (var record in batch.Records)
        {
            await _output.WriteLineAsync(JsonSerializer.Serialize(record.Record, ForwarderJson.Options));
        }
        await _output.FlushAsync(cancellationToken);

        await writer.WriteLineAsync(JsonSerializer.Serialize(new DeliveryAck
        {
            BatchId = batch.BatchId,
            Accepted = true
        }, ForwarderJson.Options).AsMemory(), cancellationToken);
    }
}
