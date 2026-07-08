namespace DeltaZulu.Agent.Runtime.Security.EtwIntegrity;

public enum EtwIntegrityPattern
{
    None = 0,
    InvalidInput = 1,
    ReadFailure = 2,

    Ret = 10,
    NopSled = 11,
    XorEaxRet = 12,
    MovEaxImmediateRet = 13,

    GenericModification = 100
}

public enum EtwBaselineSource
{
    LiveProcessStartup = 1
}

public sealed record EtwFunctionBaseline(
    string ModuleName,
    string FunctionName,
    string ModulePath,
    IntPtr LiveAddress,
    byte[] BaselineBytes,
    string BaselineSha256,
    EtwBaselineSource BaselineSource,
    DateTimeOffset CapturedAtUtc,
    int ProcessId,
    string ProcessArchitecture);

public sealed record EtwIntegrityDetectionResult(
    bool IsDetected,
    bool IsValid,
    EtwIntegrityPattern Pattern,
    string Detail,
    int? ChangedOffset = null,
    byte? ExpectedByte = null,
    byte? ActualByte = null,
    uint? ForcedReturnValue = null)
{
    public static EtwIntegrityDetectionResult Clean(string detail) =>
        new(false, true, EtwIntegrityPattern.None, detail);

    public static EtwIntegrityDetectionResult Invalid(string detail) =>
        new(false, false, EtwIntegrityPattern.InvalidInput, detail);

    public static EtwIntegrityDetectionResult Detected(
        EtwIntegrityPattern pattern,
        string detail,
        int? changedOffset = null,
        byte? expectedByte = null,
        byte? actualByte = null,
        uint? forcedReturnValue = null) =>
        new(true, true, pattern, detail, changedOffset, expectedByte, actualByte, forcedReturnValue);
}

public sealed record EtwIntegrityFinding(
    string FunctionName,
    string ModuleName,
    string ModulePath,
    IntPtr LiveAddress,
    int RegionSize,
    EtwIntegrityPattern Pattern,
    string Detail,
    DateTimeOffset ObservedAtUtc,
    byte[] BaselineBytes,
    byte[] CurrentBytes,
    string BaselineSha256,
    string CurrentSha256,
    EtwBaselineSource BaselineSource,
    int ProcessId,
    string ProcessName,
    string ProcessArchitecture,
    int? ChangedOffset,
    byte? ExpectedByte,
    byte? ActualByte,
    uint? ForcedReturnValue);

public interface IEtwIntegrityReporter
{
    ValueTask ReportAsync(
        EtwIntegrityFinding finding,
        CancellationToken cancellationToken);
}
