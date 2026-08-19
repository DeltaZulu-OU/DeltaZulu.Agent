using MessagePack;

namespace DeltaZulu.Agent.DoomDisplay;

public enum DoomPixelFormat : byte
{
    Bgr24 = 1
}

[MessagePackObject]
public sealed class DoomFramePacket
{
    [Key(0)]
    public ushort ContractVersion { get; set; } = DoomFrameCodec.ContractVersion;

    [Key(1)]
    public long Sequence { get; set; }

    [Key(2)]
    public int Width { get; set; }

    [Key(3)]
    public int Height { get; set; }

    [Key(4)]
    public DoomPixelFormat PixelFormat { get; set; } = DoomPixelFormat.Bgr24;

    [Key(5)]
    public byte[] Pixels { get; set; } = [];

    [Key(6)]
    public long CapturedAtUnixTimeMilliseconds { get; set; }
}

public static class DoomFrameCodec
{
    public const ushort ContractVersion = 1;
    public const int BytesPerPixel = 3;
    public const int MaximumWidth = 320;
    public const int MaximumHeight = 200;
    public const int MaximumPixelBytes = MaximumWidth * MaximumHeight * BytesPerPixel;

    public static byte[] Encode(DoomFramePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        Validate(packet);
        return MessagePackSerializer.Serialize(packet);
    }

    public static DoomFramePacket Decode(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            throw new InvalidDataException("Doom-frame payload is empty.");
        }

        var packet = MessagePackSerializer.Deserialize<DoomFramePacket>(payload.ToArray());
        Validate(packet);
        return packet;
    }

    public static void Validate(DoomFramePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (packet.ContractVersion != ContractVersion)
        {
            throw new InvalidDataException($"Unsupported Doom-frame contract version {packet.ContractVersion}.");
        }

        if (packet.PixelFormat != DoomPixelFormat.Bgr24)
        {
            throw new InvalidDataException($"Unsupported Doom-frame pixel format {packet.PixelFormat}.");
        }

        if (packet.Width is < 1 or > MaximumWidth || packet.Height is < 1 or > MaximumHeight)
        {
            throw new InvalidDataException(
                $"Frame dimensions {packet.Width}x{packet.Height} exceed the allowed range 1..{MaximumWidth} by 1..{MaximumHeight}.");
        }

        var expectedPixels = checked(packet.Width * packet.Height * BytesPerPixel);
        if (packet.Pixels is null || packet.Pixels.Length != expectedPixels || packet.Pixels.Length > MaximumPixelBytes)
        {
            throw new InvalidDataException(
                $"Bgr24 frame {packet.Width}x{packet.Height} requires exactly {expectedPixels} pixel bytes.");
        }
    }
}
