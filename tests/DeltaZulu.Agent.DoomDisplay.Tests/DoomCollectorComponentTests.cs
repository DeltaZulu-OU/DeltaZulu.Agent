using DeltaZulu.Agent.DoomDisplay.Inputs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DeltaZulu.Agent.DoomDisplay.Tests;

[TestClass]
public sealed class DoomCollectorComponentTests
{
    [TestMethod]
    public void InputDecoder_DecodesValidatedMessagePackFrame()
    {
        var source = CreatePacket(12, 7);
        var decoder = new DoomInputDecoder();

        var decoded = decoder.Decode(DoomFrameCodec.Encode(source));

        Assert.AreEqual(12L, decoded.Sequence);
        CollectionAssert.AreEqual(source.Pixels, decoded.Pixels);
    }

    [TestMethod]
    public void InputDecoder_RejectsMalformedPayloadWithCollectorSpecificException()
    {
        var decoder = new DoomInputDecoder();

        _ = Assert.ThrowsExactly<DoomInputDecodeException>(() => decoder.Decode(new byte[] { 0xC1 }));
    }

    [TestMethod]
    public void DoubleBuffer_ReplacesUnrenderedFrameWithNewestSequence()
    {
        var buffer = new LatestFrameDoubleBuffer();
        buffer.Submit(CreatePacket(1, 10));
        buffer.Submit(CreatePacket(2, 20));

        Assert.IsTrue(buffer.TrySwapForRender(out var rendered));
        Assert.IsNotNull(rendered);
        Assert.AreEqual(2L, rendered.Sequence);
        Assert.AreEqual((byte)20, rendered.Pixels[0]);

        var metrics = buffer.GetMetrics();
        Assert.AreEqual(2L, metrics.ReceivedFrames);
        Assert.AreEqual(1L, metrics.ReplacedPendingFrames);
        Assert.AreEqual(0L, metrics.RenderedFrames);
    }

    [TestMethod]
    public void DoubleBuffer_CopiesInputBeforeAcknowledgementBoundary()
    {
        var buffer = new LatestFrameDoubleBuffer();
        var source = CreatePacket(7, 33);
        buffer.Submit(source);
        source.Pixels[0] = 99;

        Assert.IsTrue(buffer.TrySwapForRender(out var rendered));
        Assert.IsNotNull(rendered);
        Assert.AreEqual((byte)33, rendered.Pixels[0]);
    }

    [TestMethod]
    public void DoubleBuffer_RecordsPresentationAfterSuccessfulRender()
    {
        var buffer = new LatestFrameDoubleBuffer();
        buffer.Submit(CreatePacket(23, 40));
        Assert.IsTrue(buffer.TrySwapForRender(out var rendered));
        Assert.IsNotNull(rendered);

        var metrics = buffer.RecordRendered(rendered.Sequence);

        Assert.AreEqual(1L, metrics.RenderedFrames);
        Assert.AreEqual(23L, metrics.LastRenderedSequence);
    }

    private static DoomFramePacket CreatePacket(long sequence, byte blue) => new() {
        Sequence = sequence,
        Width = 1,
        Height = 1,
        PixelFormat = DoomPixelFormat.Bgr24,
        Pixels = [blue, 0, 0]
    };
}
