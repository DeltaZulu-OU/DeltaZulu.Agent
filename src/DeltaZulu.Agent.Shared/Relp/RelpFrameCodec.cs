using System.Buffers;
using System.Globalization;
using System.Text;

namespace DeltaZulu.Agent.Shared.Relp;

public readonly record struct RelpFrame(int TransactionId, string Command, ReadOnlyMemory<byte> Payload);

public static class RelpFrameCodec
{
    public static async Task<RelpFrame?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default)
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
        var length = int.Parse(lengthText, CultureInfo.InvariantCulture);
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);

        var terminator = new byte[1];
        await stream.ReadExactlyAsync(terminator, cancellationToken).ConfigureAwait(false);
        if (terminator[0] != (byte)'\n')
        {
            throw new InvalidDataException("Missing RELP frame terminator.");
        }

        return new RelpFrame(int.Parse(transactionIdText, CultureInfo.InvariantCulture), command, payload);
    }

    public static RelpFrame ReadFrameOrThrow(RelpFrame? frame) =>
        frame ?? throw new EndOfStreamException();

    public static async Task WriteResponseAsync(Stream stream, int transactionId, string payload, CancellationToken cancellationToken = default)
    {
        var body = Encoding.UTF8.GetBytes(payload);
        var header = Encoding.ASCII.GetBytes($"{transactionId} rsp {body.Length} ");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReadTokenAsync(Stream stream, byte delimiter, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(256);
        try
        {
            var position = 0;
            var one = new byte[1];
            while (true)
            {
                var read = await stream.ReadAsync(one.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return position == 0 ? null : Encoding.ASCII.GetString(buffer, 0, position);
                }

                if (one[0] == delimiter)
                {
                    return Encoding.ASCII.GetString(buffer, 0, position);
                }

                if (position >= buffer.Length)
                {
                    var larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    Buffer.BlockCopy(buffer, 0, larger, 0, position);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                buffer[position++] = one[0];
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
