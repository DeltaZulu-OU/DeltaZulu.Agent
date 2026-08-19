using DeltaZulu.Agent.DoomDisplay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DeltaZulu.Agent.DoomDisplay.Tests;

[TestClass]
public sealed class DoomFrameCodecTests
{
    [TestMethod]
    public void RoundTrip_PreservesSequenceDimensionsAndBgr24Pixels()
    {
        var packet = new DoomFramePacket {
            Sequence = 42,
            Width = 2,
            Height = 2,
            PixelFormat = DoomPixelFormat.Bgr24,
            Pixels = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]
        };

        var roundTripped = DoomFrameCodec.Decode(DoomFrameCodec.Encode(packet));

        Assert.AreEqual(42L, roundTripped.Sequence);
        Assert.AreEqual(2, roundTripped.Width);
        Assert.AreEqual(2, roundTripped.Height);
        Assert.AreEqual(DoomPixelFormat.Bgr24, roundTripped.PixelFormat);
        CollectionAssert.AreEqual(packet.Pixels, roundTripped.Pixels);
    }

    [TestMethod]
    public void Encode_RejectsWrongPixelBufferLength()
    {
        var packet = new DoomFramePacket {
            Width = 2,
            Height = 2,
            Pixels = [0, 1, 2]
        };

        _ = Assert.ThrowsExactly<InvalidDataException>(() => DoomFrameCodec.Encode(packet));
    }

    [TestMethod]
    public void Encode_RejectsDimensionsBeyondRetroContractLimit()
    {
        var packet = new DoomFramePacket {
            Width = DoomFrameCodec.MaximumWidth + 1,
            Height = 1,
            Pixels = [0, 0, 0]
        };

        _ = Assert.ThrowsExactly<InvalidDataException>(() => DoomFrameCodec.Encode(packet));
    }
}
