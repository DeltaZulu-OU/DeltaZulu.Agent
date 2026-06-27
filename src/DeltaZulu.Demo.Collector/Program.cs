using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DeltaZulu.Agent.Shared.Pipeline.Delivery;
using DeltaZulu.Agent.Shared.Pipeline.Ndjson;
using DeltaZulu.Agent.Shared.Pipeline.Relp;

namespace DeltaZulu.Demo.Collector;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var address = IPAddress.Parse(GetOption(args, "--address") ?? "127.0.0.1");
        var port = int.Parse(GetOption(args, "--port") ?? "6514", System.Globalization.CultureInfo.InvariantCulture);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) => {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var collector = new DemoRelpCollector(address, port, Console.Out);
        await collector.RunAsync(cts.Token).ConfigureAwait(false);
        return 0;
    }

    private static string? GetOption(string[] args, string option)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            if (token.StartsWith(option + "=", StringComparison.OrdinalIgnoreCase))
            {
                return token[(option.Length + 1)..];
            }

            if (token.Equals(option, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"{option} requires a value.");
                }

                return args[index + 1];
            }
        }

        return null;
    }

    private static void PrintUsage() => Console.WriteLine("""
Usage:
  dzdemo-collector [--address <ip>] [--port <port>]

Receives RELP syslog frames from dzagentd, prints decoded
DeltaZulu delivery batches, and replies with RELP rsp 200 acknowledgements.
""");
}

internal sealed class DemoRelpCollector
{
    private readonly IPAddress _address;
    private readonly int _port;
    private readonly TextWriter _output;

    public DemoRelpCollector(IPAddress address, int port, TextWriter output)
    {
        _address = address;
        _port = port;
        _output = output;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(_address, _port);
        listener.Start();
        await _output.WriteLineAsync($"DeltaZulu demo RELP collector listening on {_address}:{_port}").ConfigureAwait(false);
        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
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

        while (!cancellationToken.IsCancellationRequested)
        {
            var maybeFrame = await RelpFrameCodec.ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (maybeFrame is not { } frame)
            {
                return;
            }

            switch (frame.Command)
            {
                case "open":
                    await RelpFrameCodec.WriteResponseAsync(stream, frame.TransactionId, "200 OK\nrelp_version=0\ncommands=syslog", cancellationToken).ConfigureAwait(false);
                    break;
                case "syslog":
                    await PrintBatchAsync(frame.Payload, cancellationToken).ConfigureAwait(false);
                    await RelpFrameCodec.WriteResponseAsync(stream, frame.TransactionId, "200 OK", cancellationToken).ConfigureAwait(false);
                    break;
                case "close":
                    await RelpFrameCodec.WriteResponseAsync(stream, frame.TransactionId, "200 OK", cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    await RelpFrameCodec.WriteResponseAsync(stream, frame.TransactionId, $"500 unsupported command {frame.Command}", cancellationToken).ConfigureAwait(false);
                    return;
            }
        }
    }

    private async Task PrintBatchAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var batch = JsonSerializer.Deserialize<DeliveryBatch>(payload.Span, NdjsonSerializerOptions.CreateDefault());
        if (batch is null)
        {
            await _output.WriteLineAsync("invalid DeltaZulu delivery batch payload").ConfigureAwait(false);
            return;
        }

        await _output.WriteLineAsync($"batch {batch.BatchId} records={batch.Records.Count} createdAt={batch.CreatedAt:O}").ConfigureAwait(false);
        foreach (var record in batch.Records)
        {
            await _output.WriteLineAsync(JsonSerializer.Serialize(record.Record, NdjsonSerializerOptions.CreateDefault())).ConfigureAwait(false);
        }

        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

}
