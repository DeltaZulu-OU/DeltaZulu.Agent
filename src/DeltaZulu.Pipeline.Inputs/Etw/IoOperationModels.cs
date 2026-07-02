namespace DeltaZulu.Pipeline.Inputs.Etw;

public enum FileIoOpcode
{
    CreateFile = 64,
    Cleanup = 65,
    Close = 66,
    ReadFile = 67,
    WriteFile = 68,
    SetInformation = 69,
    DeleteFile = 70,
    RenameFile = 71,
    DirectoryEnumeration = 72,
    Flush = 73,
    QueryFileInformation = 74,
    FilesystemControlEvent = 75,
    EndOperation = 76,
    DirectoryNotification = 77,
    DeletePath = 79,
    RenamePath = 80,
    FltRead = 83,
    FltWrite = 84,
    FltSetInfo = 85,
    FltQueryInfo = 86
}

public enum OperationCorrelationSource
{
    IrpStartEnd,
    MissingStart,
    MissingEnd,
    WithoutIrp,
    IrpReused,
    Unknown
}

public sealed record StartedIoOperation(
    ulong Irp,
    string Operation,
    DateTimeOffset TimestampUtc,
    int ProcessId,
    int ThreadId,
    ulong? FileObject,
    ulong? FileKey,
    int? OperationCode = null,
    string? OperationName = null,
    string? OperationFamily = "File",
    string? OperationNameSource = FileIoOpcodeLookup.OperationNameSource);

public sealed record IoOperationCorrelation(
    ulong? Irp,
    string? Operation,
    OperationCorrelationSource OperationCorrelationSource,
    DateTimeOffset? OperationStartUtc,
    DateTimeOffset? OperationEndUtc,
    double? OperationDurationMs,
    uint? NtStatus,
    ulong? ExtraInfo,
    bool MissingStartEvent,
    bool MissingEndEvent,
    bool IrpReusedBeforeEnd,
    StartedIoOperation? StartedOperation = null,
    StartedIoOperation? DisplacedOperation = null,
    int? OperationCode = null,
    string? OperationName = null,
    string? OperationFamily = null,
    string? OperationNameSource = null)
{
    public static IoOperationCorrelation Started(StartedIoOperation started) => new(
        started.Irp,
        started.Operation,
        OperationCorrelationSource.Unknown,
        started.TimestampUtc,
        null,
        null,
        null,
        null,
        MissingStartEvent: false,
        MissingEndEvent: false,
        IrpReusedBeforeEnd: false,
        StartedOperation: started,
        OperationCode: started.OperationCode,
        OperationName: started.OperationName ?? started.Operation,
        OperationFamily: started.OperationFamily,
        OperationNameSource: started.OperationNameSource);

    public static IoOperationCorrelation WithoutIrp(string operation) => new(
        null,
        operation,
        OperationCorrelationSource.WithoutIrp,
        null,
        null,
        null,
        null,
        null,
        MissingStartEvent: false,
        MissingEndEvent: false,
        IrpReusedBeforeEnd: false,
        OperationName: operation);

    public static IoOperationCorrelation UnmatchedEndWithoutIrp(uint? ntStatus, ulong? extraInfo) => new(
        null,
        null,
        OperationCorrelationSource.WithoutIrp,
        null,
        null,
        null,
        ntStatus,
        extraInfo,
        MissingStartEvent: true,
        MissingEndEvent: false,
        IrpReusedBeforeEnd: false);

    public static IoOperationCorrelation MissingStart(ulong irp, uint? ntStatus, ulong? extraInfo, DateTimeOffset endUtc) => new(
        irp,
        null,
        OperationCorrelationSource.MissingStart,
        null,
        endUtc,
        null,
        ntStatus,
        extraInfo,
        MissingStartEvent: true,
        MissingEndEvent: false,
        IrpReusedBeforeEnd: false);

    public static IoOperationCorrelation Completed(
        StartedIoOperation started,
        DateTimeOffset endUtc,
        uint? ntStatus,
        ulong? extraInfo) => new(
            started.Irp,
            started.Operation,
            OperationCorrelationSource.IrpStartEnd,
            started.TimestampUtc,
            endUtc,
            Math.Max(0, (endUtc - started.TimestampUtc).TotalMilliseconds),
            ntStatus,
            extraInfo,
            MissingStartEvent: false,
            MissingEndEvent: false,
            IrpReusedBeforeEnd: false,
            StartedOperation: started,
            OperationCode: started.OperationCode,
            OperationName: started.OperationName ?? started.Operation,
            OperationFamily: started.OperationFamily,
            OperationNameSource: started.OperationNameSource);

    public static IoOperationCorrelation MissingEnd(StartedIoOperation started) => new(
        started.Irp,
        started.Operation,
        OperationCorrelationSource.MissingEnd,
        started.TimestampUtc,
        null,
        null,
        null,
        null,
        MissingStartEvent: false,
        MissingEndEvent: true,
        IrpReusedBeforeEnd: false,
        StartedOperation: started,
        OperationCode: started.OperationCode,
        OperationName: started.OperationName ?? started.Operation,
        OperationFamily: started.OperationFamily,
        OperationNameSource: started.OperationNameSource);

    public static IoOperationCorrelation IrpReused(StartedIoOperation displaced, StartedIoOperation replacement) => new(
        displaced.Irp,
        displaced.Operation,
        OperationCorrelationSource.IrpReused,
        displaced.TimestampUtc,
        null,
        null,
        null,
        null,
        MissingStartEvent: false,
        MissingEndEvent: true,
        IrpReusedBeforeEnd: true,
        StartedOperation: replacement,
        DisplacedOperation: displaced,
        OperationCode: displaced.OperationCode,
        OperationName: displaced.OperationName ?? displaced.Operation,
        OperationFamily: displaced.OperationFamily,
        OperationNameSource: displaced.OperationNameSource);
}

public static class FileIoOpcodeLookup
{
    public const string OperationNameSource = "DeltaZulu.FileIoOpcodeMap";
    private static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
    {
        [(int)FileIoOpcode.CreateFile] = nameof(FileIoOpcode.CreateFile),
        [(int)FileIoOpcode.Cleanup] = nameof(FileIoOpcode.Cleanup),
        [(int)FileIoOpcode.Close] = nameof(FileIoOpcode.Close),
        [(int)FileIoOpcode.ReadFile] = nameof(FileIoOpcode.ReadFile),
        [(int)FileIoOpcode.WriteFile] = nameof(FileIoOpcode.WriteFile),
        [(int)FileIoOpcode.SetInformation] = nameof(FileIoOpcode.SetInformation),
        [(int)FileIoOpcode.DeleteFile] = nameof(FileIoOpcode.DeleteFile),
        [(int)FileIoOpcode.RenameFile] = nameof(FileIoOpcode.RenameFile),
        [(int)FileIoOpcode.DirectoryEnumeration] = nameof(FileIoOpcode.DirectoryEnumeration),
        [(int)FileIoOpcode.Flush] = nameof(FileIoOpcode.Flush),
        [(int)FileIoOpcode.QueryFileInformation] = nameof(FileIoOpcode.QueryFileInformation),
        [(int)FileIoOpcode.FilesystemControlEvent] = nameof(FileIoOpcode.FilesystemControlEvent),
        [(int)FileIoOpcode.EndOperation] = nameof(FileIoOpcode.EndOperation),
        [(int)FileIoOpcode.DirectoryNotification] = nameof(FileIoOpcode.DirectoryNotification),
        [(int)FileIoOpcode.DeletePath] = nameof(FileIoOpcode.DeletePath),
        [(int)FileIoOpcode.RenamePath] = nameof(FileIoOpcode.RenamePath),
        [(int)FileIoOpcode.FltRead] = nameof(FileIoOpcode.FltRead),
        [(int)FileIoOpcode.FltWrite] = nameof(FileIoOpcode.FltWrite),
        [(int)FileIoOpcode.FltSetInfo] = nameof(FileIoOpcode.FltSetInfo),
        [(int)FileIoOpcode.FltQueryInfo] = nameof(FileIoOpcode.FltQueryInfo)
    };

    public static string? GetName(int opcode) => Names.GetValueOrDefault(opcode);
}
