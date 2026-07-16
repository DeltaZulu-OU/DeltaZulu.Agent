namespace DeltaZulu.Pipeline.Inputs.Etw;

public enum FileResolutionSource
{
    NativePayload,
    FileIoCreate,
    FileIoName,
    FileIoRundown,
    KernelFileCreate,
    KernelFileRename,
    KernelFileDeletePath,
    FileObjectCache,
    FileKeyCache,
    DelayedCache,
    Unknown
}

public enum ResolutionConfidence
{
    High,
    Medium,
    Low,
    Unknown
}

public readonly record struct FileIdentityKey(ulong? FileObject, ulong? FileKey)
{
    public bool HasValue => FileObject.HasValue || FileKey.HasValue;

    public override string ToString() => (FileObject, FileKey) switch {
        ( { } fileObject, { } fileKey) => $"FileObject=0x{fileObject:x};FileKey=0x{fileKey:x}",
        ( { } fileObject, null) => $"FileObject=0x{fileObject:x}",
        (null, { } fileKey) => $"FileKey=0x{fileKey:x}",
        _ => "Unknown"
    };
}

public sealed record FileIdentityState(
    ulong? FileObject,
    ulong? FileKey,
    string Path,
    FileResolutionSource Source,
    ResolutionConfidence Confidence,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    bool Deleted = false);

public sealed record FileIdentityResolution(
    string? ResolvedFilePath,
    FileResolutionSource FileResolutionSource,
    ResolutionConfidence FileResolutionConfidence,
    long? FileResolutionAgeMs,
    string FileResolverKey,
    bool FileResolverMiss)
{
    public static FileIdentityResolution FromNativePayload(
        string path,
        FileIdentityKey key,
        DateTimeOffset observedAtUtc) => new(
            path,
            global::DeltaZulu.Pipeline.Inputs.Etw.FileResolutionSource.NativePayload,
            ResolutionConfidence.High,
            0,
            key.ToString(),
            FileResolverMiss: false);

    public static FileIdentityResolution FromState(
        FileIdentityState state,
        FileResolutionSource resolutionSource,
        DateTimeOffset observedAtUtc) => new(
            state.Path,
            resolutionSource,
            state.Confidence,
            Math.Max(0, (long)(observedAtUtc - state.LastSeenUtc).TotalMilliseconds),
            new FileIdentityKey(state.FileObject, state.FileKey).ToString(),
            FileResolverMiss: false);

    public static FileIdentityResolution Unresolved(FileIdentityKey key) => new(
        null,
        global::DeltaZulu.Pipeline.Inputs.Etw.FileResolutionSource.Unknown,
        ResolutionConfidence.Unknown,
        null,
        key.ToString(),
        FileResolverMiss: key.HasValue);
}

public readonly record struct ProcessIdentityKey(int ProcessId, DateTimeOffset? StartTimeUtc)
{
    public override string ToString() => StartTimeUtc.HasValue
        ? $"ProcessId={ProcessId};StartTimeUtc={StartTimeUtc.Value:O}"
        : $"ProcessId={ProcessId}";
}

public sealed record ProcessIdentityState(
    ProcessIdentityKey Key,
    int? ParentProcessId,
    string? ImagePath,
    string? CommandLine,
    string? UserSid,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    ResolutionConfidence Confidence);

public sealed record ProcessIdentityResolution(
    string? ResolvedProcessImage,
    string? ResolvedProcessCommandLine,
    string ProcessResolutionSource,
    ResolutionConfidence ProcessResolutionConfidence,
    long? ProcessResolutionAgeMs,
    string ProcessGenerationKey);
