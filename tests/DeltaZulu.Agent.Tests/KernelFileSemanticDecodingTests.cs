using DeltaZulu.Pipeline.Core.Etw;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class KernelFileSemanticDecodingTests
{
    private static readonly KernelFileEventContext OperationEndContext = new()
    {
        ProviderGuid = KernelFileEventContext.ProviderGuidValue,
        EventId = 24,
        Version = 0,
        EventName = "OperationEnd",
        TaskName = "OperationEnd",
        Status = 0,
        MatchedStartOperation = "Read"
    };

    [TestMethod]
    public void DecodeExtraInformation_KnownReparseTag_EmitsRawHexAndDecodedName()
    {
        var decoder = new KernelFileScalarDecoder();

        var result = decoder.TryDecode(OperationEndContext, "ExtraInformation", 2684354563);

        Assert.AreEqual(2684354563UL, result.RawValue);
        Assert.AreEqual("0x00000000A0000003", result.HexValue);
        Assert.AreEqual(KernelFileScalarKind.ReparseTag, result.Kind);
        Assert.AreEqual("IO_REPARSE_TAG_MOUNT_POINT", result.DecodedName);
        Assert.AreEqual(KernelFileScalarDecodeStatus.Decoded, result.DecodeStatus);
        Assert.AreEqual(KernelFileScalarDecoder.DecoderName, result.DecoderName);
    }

    [TestMethod]
    public void DecodeIrp_EmitsCorrelationOnlyAndDoesNotDereference()
    {
        var decoder = new KernelFileScalarDecoder();

        var result = decoder.TryDecode(OperationEndContext, "Irp", 0xFFFFB203B649C160);

        Assert.AreEqual("0xFFFFB203B649C160", result.HexValue);
        Assert.AreEqual(KernelFileScalarKind.IrpCorrelationKey, result.Kind);
        Assert.AreEqual(KernelFileScalarDecodeStatus.CorrelationOnly, result.DecodeStatus);
        Assert.IsNull(result.DecodedName);
    }

    [TestMethod]
    public void DecodeFileObject_EmitsCorrelationOnlyAndDoesNotDereference()
    {
        var decoder = new KernelFileScalarDecoder();

        var result = decoder.TryDecode(OperationEndContext, "FileObject", 0xFFFFB203B649C160);

        Assert.AreEqual(KernelFileScalarKind.FileObjectKey, result.Kind);
        Assert.AreEqual(KernelFileScalarDecodeStatus.CorrelationOnly, result.DecodeStatus);
    }

    [TestMethod]
    public void DecodeFileKey_EmitsCorrelationOnlyAndDoesNotDereference()
    {
        var decoder = new KernelFileScalarDecoder();

        var result = decoder.TryDecode(OperationEndContext, "FileKey", 0x0002000000123456);

        Assert.AreEqual("0x0002000000123456", result.HexValue);
        Assert.AreEqual(KernelFileScalarKind.FileKey, result.Kind);
        Assert.AreEqual(KernelFileScalarDecodeStatus.CorrelationOnly, result.DecodeStatus);
    }

    [TestMethod]
    public void DecodeLegacyThreadId_DecodesWhenUInt32Compatible()
    {
        var decoder = new KernelFileScalarDecoder();

        var result = decoder.TryDecode(OperationEndContext, "ThreadId", 9124);

        Assert.AreEqual(KernelFileScalarKind.LegacyThreadId, result.Kind);
        Assert.AreEqual(KernelFileScalarDecodeStatus.Decoded, result.DecodeStatus);
        Assert.AreEqual((uint)9124, result.DecodedValue);
    }

    [TestMethod]
    public void DecodeIssuingThreadId_DecodesAsNativeThreadId()
    {
        var decoder = new KernelFileScalarDecoder();

        var result = decoder.TryDecode(OperationEndContext, "IssuingThreadId", 9124);

        Assert.AreEqual(KernelFileScalarKind.ThreadId, result.Kind);
        Assert.AreEqual(KernelFileScalarDecodeStatus.Decoded, result.DecodeStatus);
        Assert.AreEqual((uint)9124, result.DecodedValue);
    }

    [TestMethod]
    public void DecodeUnknownExtraInformation_EmitsRawOnly()
    {
        var decoder = new KernelFileScalarDecoder();

        var result = decoder.TryDecode(OperationEndContext, "ExtraInformation", 42);

        Assert.AreEqual("0x000000000000002A", result.HexValue);
        Assert.AreEqual(KernelFileScalarKind.OperationCompletionInformation, result.Kind);
        Assert.AreEqual(KernelFileScalarDecodeStatus.RawOnly, result.DecodeStatus);
    }

    [TestMethod]
    public void DecodeKnownNtStatus_EmitsStatusName()
    {
        var decoder = new KernelFileScalarDecoder();

        var result = decoder.TryDecode(OperationEndContext, "Status", 0);

        Assert.AreEqual("0x0000000000000000", result.HexValue);
        Assert.AreEqual(KernelFileScalarKind.NtStatus, result.Kind);
        Assert.AreEqual("STATUS_SUCCESS", result.DecodedName);
        Assert.AreEqual(KernelFileScalarDecodeStatus.Decoded, result.DecodeStatus);
    }

    [TestMethod]
    public void DecodeKnownInfoClass_EmitsInfoClassName()
    {
        var decoder = new KernelFileScalarDecoder();

        var result = decoder.TryDecode(OperationEndContext, "InfoClass", 10);

        Assert.AreEqual(KernelFileScalarKind.FileInformationClass, result.Kind);
        Assert.AreEqual("FileRenameInformation", result.DecodedName);
        Assert.AreEqual(KernelFileScalarDecodeStatus.Decoded, result.DecodeStatus);
    }

    [TestMethod]
    public void KernelFileEventIdLookup_UsesManifestEventIdSpace()
    {
        Assert.AreEqual("Create", KernelFileEventIdLookup.GetName(12));
        Assert.AreEqual("OperationEnd", KernelFileEventIdLookup.GetName(24));
        Assert.IsNull(KernelFileEventIdLookup.GetName(67));
    }
}
