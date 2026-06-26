using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DeltaZulu.Agent.Forwarder;
using DeltaZulu.Agent.Outputs.Ndjson;

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

Receives RELP syslog frames from dzagent forwarder output, prints decoded
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
            var frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                return;
            }

            var (transactionId, command, payload) = frame.Value;
            switch (command)
            {
                case "open":
                    await WriteResponseAsync(stream, transactionId, "200 OK\nrelp_version=0\ncommands=syslog", cancellationToken).ConfigureAwait(false);
                    break;
                case "syslog":
                    await PrintBatchAsync(payload, cancellationToken).ConfigureAwait(false);
                    await WriteResponseAsync(stream, transactionId, "200 OK", cancellationToken).ConfigureAwait(false);
                    break;
                case "close":
                    await WriteResponseAsync(stream, transactionId, "200 OK", cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    await WriteResponseAsync(stream, transactionId, $"500 unsupported command {command}", cancellationToken).ConfigureAwait(false);
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

    private static async Task<(int TransactionId, string Command, ReadOnlyMemory<byte> Payload)?> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var transactionIdText = await ReadTokenAsync(stream, (byte)' ', cancellationToken).ConfigureAwait(false);
        if (transactionIdText is null)
        {
            return null;
        }

        var command = await ReadTokenAsync(stream, (byte)' ', cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Missing RELP command.");
        var lengthText = await ReadTokenAsync(stream, (byte)' ', cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Missing RELP payload length.");
        var length = int.Parse(lengthText, System.Globalization.CultureInfo.InvariantCulture);
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);

        var newline = new byte[1];
        await stream.ReadExactlyAsync(newline, cancellationToken).ConfigureAwait(false);
        if (newline[0] != (byte)'\n')
        {
            throw new InvalidDataException("Missing RELP frame terminator.");
        }

        return (int.Parse(transactionIdText, System.Globalization.CultureInfo.InvariantCulture), command, payload);
    }

    private static async Task<string?> ReadTokenAsync(Stream stream, byte delimiter, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.Count == 0 ? null : Encoding.ASCII.GetString(buffer.ToArray());
            }

            if (one[0] == delimiter)
            {
                return Encoding.ASCII.GetString(buffer.ToArray());
            }

            buffer.Add(one[0]);
        }
    }

    private static async Task WriteResponseAsync(Stream stream, int transactionId, string payload, CancellationToken cancellationToken)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var headerBytes = Encoding.ASCII.GetBytes($"{transactionId} rsp {payloadBytes.Length} ");
        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
