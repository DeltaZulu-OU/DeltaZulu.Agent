using System.Buffers;
using System.Globalization;
using System.Text;

namespace DeltaZulu.Pipeline.Forwarder;

public readonly record struct ForwarderFrame(int TransactionId, string Command, ReadOnlyMemory<byte> Payload);

public static class ForwarderFrameCodec
{
    public static async Task<ForwarderFrame?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var transactionIdText = await ReadTokenAsync(stream, (byte)' ', cancellationToken).ConfigureAwait(false);
        if (transactionIdText is null)
        {
            return null;
        }

        var command = await ReadTokenAsync(stream, (byte)' ', cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Missing FORWARDER command.");
        var lengthText = await ReadTokenAsync(stream, (byte)' ', cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Missing FORWARDER payload length.");
        var length = int.Parse(lengthText, CultureInfo.InvariantCulture);
        var payload = new byte[length];
        if (!await TryReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var terminator = new byte[1];
        if (!await TryReadExactlyAsync(stream, terminator, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }
        if (terminator[0] != (byte)'\n')
        {
            throw new InvalidDataException("Missing FORWARDER frame terminator.");
        }

        return new ForwarderFrame(int.Parse(transactionIdText, CultureInfo.InvariantCulture), command, payload);
    }

    public static ForwarderFrame ReadFrameOrThrow(ForwarderFrame? frame) =>
        frame ?? throw new EndOfStreamException();


    public static async Task WriteFrameAsync(Stream stream, int transactionId, string command, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        var header = Encoding.ASCII.GetBytes($"{transactionId} {command} {payload.Length} ");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

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

    private static async Task<bool> TryReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        while (buffer.Length > 0)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            buffer = buffer[read..];
        }

        return true;
    }
}
