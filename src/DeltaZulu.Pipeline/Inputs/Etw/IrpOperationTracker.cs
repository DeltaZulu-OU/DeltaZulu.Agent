namespace DeltaZulu.Pipeline.Inputs.Etw;

public sealed class IrpOperationTracker
{
    private readonly Lock _gate = new();
    private readonly int _maximumActiveOperations;
    private readonly Dictionary<ulong, StartedIoOperation> _started = [];

    public IrpOperationTracker(int maximumActiveOperations = 100_000)
    {
        if (maximumActiveOperations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumActiveOperations), "Tracker capacity must be positive.");
        }

        _maximumActiveOperations = maximumActiveOperations;
    }

    public int ActiveOperationCount {
        get {
            lock (_gate)
            {
                return _started.Count;
            }
        }
    }

    public IReadOnlyList<IoOperationCorrelation> FlushIncomplete()
    {
        lock (_gate)
        {
            var incomplete = _started
                .Values
                .OrderBy(started => started.TimestampUtc)
                .Select(IoOperationCorrelation.MissingEnd)
                .ToArray();
            _started.Clear();
            return incomplete;
        }
    }

    public IoOperationCorrelation ObserveEnd(
        ulong? irp,
        DateTimeOffset timestampUtc,
        uint? ntStatus,
        ulong? extraInfo)
    {
        if (!irp.HasValue)
        {
            return IoOperationCorrelation.UnmatchedEndWithoutIrp(ntStatus, extraInfo);
        }

        lock (_gate)
        {
            if (!_started.Remove(irp.Value, out var started))
            {
                return IoOperationCorrelation.MissingStart(irp.Value, ntStatus, extraInfo, timestampUtc);
            }

            return IoOperationCorrelation.Completed(started, timestampUtc, ntStatus, extraInfo);
        }
    }

    public IReadOnlyList<IoOperationCorrelation> ObserveStart(
                ulong? irp,
        string operation,
        DateTimeOffset timestampUtc,
        int processId,
        int threadId,
        ulong? fileObject,
        ulong? fileKey) =>
        ObserveStart(irp, null, operation, timestampUtc, processId, threadId, fileObject, fileKey);

    public IReadOnlyList<IoOperationCorrelation> ObserveStart(
        ulong? irp,
        int? operationCode,
        string operationName,
        DateTimeOffset timestampUtc,
        int processId,
        int threadId,
        ulong? fileObject,
        ulong? fileKey,
        string operationFamily = "File",
        string operationNameSource = FileIoOpcodeLookup.OperationNameSource)
    {
        if (!irp.HasValue)
        {
            return [IoOperationCorrelation.WithoutIrp(operationName) with {
                OperationCode = operationCode,
                OperationName = operationName,
                OperationFamily = operationFamily,
                OperationNameSource = operationNameSource
            }];
        }

        var started = new StartedIoOperation(
            irp.Value,
            operationName,
            timestampUtc,
            processId,
            threadId,
            fileObject,
            fileKey,
            operationCode,
            operationName,
            operationFamily,
            operationNameSource);

        lock (_gate)
        {
            var correlations = new List<IoOperationCorrelation>();
            if (_started.Remove(irp.Value, out var previous))
            {
                correlations.Add(IoOperationCorrelation.IrpReused(previous, started));
            }

            _started[irp.Value] = started;
            correlations.Add(IoOperationCorrelation.Started(started));
            FlushOverflow(correlations);
            return correlations;
        }
    }

    private void FlushOverflow(List<IoOperationCorrelation> correlations)
    {
        while (_started.Count > _maximumActiveOperations)
        {
            var oldest = _started.MinBy(pair => pair.Value.TimestampUtc);
            _started.Remove(oldest.Key);
            correlations.Add(IoOperationCorrelation.MissingEnd(oldest.Value));
        }
    }
}
