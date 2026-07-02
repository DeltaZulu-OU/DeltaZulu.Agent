namespace DeltaZulu.Pipeline.Inputs.Etw;

public enum KernelFileScalarDecodeStatus
{
    Decoded,
    RawOnly,
    CorrelationOnly,
    KnownOpaqueHandle,
    UnsupportedContext,
    NotApplicable,
    Invalid
}

public enum KernelFileScalarKind
{
    IrpCorrelationKey,
    FileObjectKey,
    FileKey,
    LegacyThreadId,
    ThreadId,
    OperationCompletionInformation,
    InfoClassSpecificValue,
    ReparseTag,
    FsctlCode,
    NtStatus,
    FileInformationClass,
    CreateOptionsFlags,
    FileAttributesFlags,
    ShareAccessFlags,
    IoFlags,
    UnknownPointerSizedValue
}

public sealed record KernelFileEventContext
{
    public static readonly Guid ProviderGuidValue = Guid.Parse("edd08927-9cc4-4e65-b970-c2560fb5c289");

    public required Guid ProviderGuid { get; init; }
    public required int EventId { get; init; }
    public required int Version { get; init; }
    public required string EventName { get; init; }
    public string? TaskName { get; init; }
    public int? InfoClass { get; init; }
    public uint? Status { get; init; }
    public string? MatchedStartOperation { get; init; }
}

public sealed record KernelFileScalarDecodeResult
{
    public required ulong RawValue { get; init; }
    public required string HexValue { get; init; }
    public required KernelFileScalarKind Kind { get; init; }
    public string? DecodedName { get; init; }
    public object? DecodedValue { get; init; }
    public required KernelFileScalarDecodeStatus DecodeStatus { get; init; }
    public required string DecoderName { get; init; }
}

public interface IKernelFileScalarDecoder
{
    KernelFileScalarDecodeResult TryDecode(
        KernelFileEventContext context,
        string fieldName,
        ulong rawValue);
}

public sealed class KernelFileScalarDecoder : IKernelFileScalarDecoder
{
    public const string DecoderName = "DeltaZulu.KernelFile.ScalarDecoder";

    public KernelFileScalarDecodeResult TryDecode(
        KernelFileEventContext context,
        string fieldName,
        ulong rawValue)
    {
        if (!context.ProviderGuid.Equals(KernelFileEventContext.ProviderGuidValue))
        {
            return Result(rawValue, KernelFileScalarKind.UnknownPointerSizedValue, KernelFileScalarDecodeStatus.NotApplicable);
        }

        return fieldName switch {
            "Irp" or "IrpPtr" => Result(rawValue, KernelFileScalarKind.IrpCorrelationKey, KernelFileScalarDecodeStatus.CorrelationOnly),
            "FileObject" => Result(rawValue, KernelFileScalarKind.FileObjectKey, KernelFileScalarDecodeStatus.CorrelationOnly),
            "FileKey" => Result(rawValue, KernelFileScalarKind.FileKey, KernelFileScalarDecodeStatus.CorrelationOnly),
            "ThreadId" => DecodeLegacyThreadId(rawValue),
            "IssuingThreadId" => DecodeThreadId(rawValue),
            "Status" or "NtStatus" => DecodeNtStatus(rawValue),
            "InfoClass" => DecodeInfoClass(rawValue),
            "ExtraInformation" or "ExtraInfo" => DecodeExtraInformation(context, rawValue),
            _ => Result(rawValue, KernelFileScalarKind.UnknownPointerSizedValue, KernelFileScalarDecodeStatus.RawOnly)
        };
    }

    private static KernelFileScalarDecodeResult DecodeLegacyThreadId(ulong rawValue) => rawValue <= uint.MaxValue
        ? Result(rawValue, KernelFileScalarKind.LegacyThreadId, KernelFileScalarDecodeStatus.Decoded, decodedValue: (uint)rawValue)
        : Result(rawValue, KernelFileScalarKind.LegacyThreadId, KernelFileScalarDecodeStatus.Invalid);

    private static KernelFileScalarDecodeResult DecodeThreadId(ulong rawValue) => rawValue <= uint.MaxValue
        ? Result(rawValue, KernelFileScalarKind.ThreadId, KernelFileScalarDecodeStatus.Decoded, decodedValue: (uint)rawValue)
        : Result(rawValue, KernelFileScalarKind.ThreadId, KernelFileScalarDecodeStatus.Invalid);

    private static KernelFileScalarDecodeResult DecodeNtStatus(ulong rawValue)
    {
        var status = unchecked((uint)rawValue);
        var name = NtStatusLookup.GetName(status);
        return Result(
            rawValue,
            KernelFileScalarKind.NtStatus,
            name is null ? KernelFileScalarDecodeStatus.RawOnly : KernelFileScalarDecodeStatus.Decoded,
            name,
            status);
    }

    private static KernelFileScalarDecodeResult DecodeInfoClass(ulong rawValue)
    {
        if (rawValue > int.MaxValue)
        {
            return Result(rawValue, KernelFileScalarKind.FileInformationClass, KernelFileScalarDecodeStatus.Invalid);
        }

        var name = FileInfoClassLookup.GetName((int)rawValue);
        return Result(
            rawValue,
            KernelFileScalarKind.FileInformationClass,
            name is null ? KernelFileScalarDecodeStatus.RawOnly : KernelFileScalarDecodeStatus.Decoded,
            name,
            (int)rawValue);
    }

    private static KernelFileScalarDecodeResult DecodeExtraInformation(KernelFileEventContext context, ulong rawValue)
    {
        var reparseTag = ReparseTagLookup.GetName(unchecked((uint)rawValue));
        if (reparseTag is not null)
        {
            return Result(rawValue, KernelFileScalarKind.ReparseTag, KernelFileScalarDecodeStatus.Decoded, reparseTag, unchecked((uint)rawValue));
        }

        if (context.EventId == 24 || context.EventName.Equals("OperationEnd", StringComparison.OrdinalIgnoreCase))
        {
            return Result(rawValue, KernelFileScalarKind.OperationCompletionInformation, KernelFileScalarDecodeStatus.RawOnly);
        }

        if (context.InfoClass.HasValue)
        {
            return Result(rawValue, KernelFileScalarKind.InfoClassSpecificValue, KernelFileScalarDecodeStatus.RawOnly);
        }

        return Result(rawValue, KernelFileScalarKind.UnknownPointerSizedValue, KernelFileScalarDecodeStatus.RawOnly);
    }

    private static KernelFileScalarDecodeResult Result(
        ulong rawValue,
        KernelFileScalarKind kind,
        KernelFileScalarDecodeStatus status,
        string? decodedName = null,
        object? decodedValue = null) => new()
        {
            RawValue = rawValue,
            HexValue = KernelFilePointerFormatting.ToHex(rawValue),
            Kind = kind,
            DecodedName = decodedName,
            DecodedValue = decodedValue,
            DecodeStatus = status,
            DecoderName = DecoderName
        };
}

public static class KernelFilePointerFormatting
{
    public static string ToHex(ulong value) => $"0x{value:X16}";
}

public static class ReparseTagLookup
{
    private static readonly IReadOnlyDictionary<uint, string> Names = new Dictionary<uint, string>
    {
        [0xA0000003] = "IO_REPARSE_TAG_MOUNT_POINT",
        [0xA000000C] = "IO_REPARSE_TAG_SYMLINK",
        [0x80000005] = "IO_REPARSE_TAG_HSM",
        [0x80000006] = "IO_REPARSE_TAG_HSM2",
        [0x80000007] = "IO_REPARSE_TAG_SIS",
        [0x80000008] = "IO_REPARSE_TAG_WIM",
        [0x80000009] = "IO_REPARSE_TAG_CSV",
        [0x8000000A] = "IO_REPARSE_TAG_DFS",
        [0x80000012] = "IO_REPARSE_TAG_DFSR",
        [0x80000013] = "IO_REPARSE_TAG_DEDUP"
    };

    public static string? GetName(uint value) => Names.GetValueOrDefault(value);
}

public static class NtStatusLookup
{
    private static readonly IReadOnlyDictionary<uint, string> Names = new Dictionary<uint, string>
    {
        [0x00000000] = "STATUS_SUCCESS",
        [0x00000103] = "STATUS_PENDING",
        [0x80000005] = "STATUS_BUFFER_OVERFLOW",
        [0xC0000001] = "STATUS_UNSUCCESSFUL",
        [0xC000000D] = "STATUS_INVALID_PARAMETER",
        [0xC000000F] = "STATUS_NO_SUCH_FILE",
        [0xC0000022] = "STATUS_ACCESS_DENIED",
        [0xC0000034] = "STATUS_OBJECT_NAME_NOT_FOUND"
    };

    public static string? GetName(uint value) => Names.GetValueOrDefault(value);
}

public static class KernelFileEventIdLookup
{
    public const string OperationNameSource = "MicrosoftWindowsKernelFileManifest";

    private static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
    {
        [10] = "NameCreate",
        [11] = "NameDelete",
        [12] = "Create",
        [13] = "Cleanup",
        [14] = "Close",
        [15] = "Read",
        [16] = "Write",
        [17] = "SetInformation",
        [18] = "SetDelete",
        [19] = "Rename",
        [20] = "DirEnum",
        [21] = "Flush",
        [22] = "QueryInformation",
        [23] = "FSCTL",
        [24] = "OperationEnd",
        [25] = "DirNotify",
        [26] = "DeletePath",
        [27] = "RenamePath",
        [28] = "SetLinkPath",
        [30] = "CreateNewFile",
        [31] = "SetSecurity",
        [32] = "QuerySecurity",
        [33] = "SetEA",
        [34] = "QueryEA"
    };

    public static string? GetName(int eventId) => Names.GetValueOrDefault(eventId);
}
