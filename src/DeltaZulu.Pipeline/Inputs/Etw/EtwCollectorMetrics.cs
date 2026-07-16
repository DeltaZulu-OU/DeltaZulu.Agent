namespace DeltaZulu.Pipeline.Inputs.Etw;

public sealed class EtwCollectorMetrics
{
    private long _anonymizedThreadEvents;
    private long _cacheEvictions;
    private long _callbackExceptions;
    private long _cswitchEventsReceived;
    private long _etwCallbackEnqueueFailed;
    private long _etwCallbackEnqueueSucceeded;
    private long _etwCallbackEventsReceived;
    private long _etwCallbackEventsRejectedByNativeFilter;
    private long _etwCallbackSelfProcessEventsDropped;
    private long _etwEventsDroppedByBackpressure;
    private long _etwEventsEmitted;
    private long _etwProjectedPayloadFieldDecodeFailures;
    private long _etwProjectedPayloadFieldMissingDistinctKeys;
    private long _etwProjectedPayloadFieldMissingOccurrences;
    private long _etwWorkerEventsMaterialized;
    private long _etwWorkerMaterializationFailures;
    private long _eventsDroppedByChannel;
    private long _eventsEnqueued;
    private long _eventsLostByEtw;
    private long _eventsParsed;
    private long _eventsReceived;
    private long _fileInfoClassKnown;
    private long _fileInfoClassUnknown;
    private long _fileKeyResolverHits;
    private long _fileKeyResolverMisses;
    private long _fileObjectResolverHits;
    private long _fileObjectResolverMisses;
    private long _forwarderFailures;
    private long _ioWaitClassifiedEvents;
    private long _irpOperationsCompleted;
    private long _irpOperationsMissingEnd;
    private long _irpOperationsMissingStart;
    private long _irpOperationsStarted;
    private long _irpOperationsWithoutIrp;
    private long _irpReusedBeforeEnd;
    private long _irpTrackerEvictions;
    private long _mismatchedSchedSwitchTids;
    private long _parserFailures;
    private long _processResolverHits;
    private long _processResolverMisses;
    private long _readyThreadEventsReceived;
    private long _rundownEventsReceived;
    private long _schedulerEventsDropped;
    private long _schedulerEventsParsed;
    private long _spoolBytes;
    private long _threadStateCacheHits;
    private long _threadStateCacheMisses;
    private long _threadStateInvalid;
    private long _unknownOpcodeEvents;
    private long _unknownProviderEvents;
    private long _waitReasonInvalid;
    public long AnonymizedThreadEvents => Interlocked.Read(ref _anonymizedThreadEvents);
    public long CacheEvictions => Interlocked.Read(ref _cacheEvictions);
    public long CallbackExceptions => Interlocked.Read(ref _callbackExceptions);
    public long CswitchEventsReceived => Interlocked.Read(ref _cswitchEventsReceived);
    public long EtwCallbackEnqueueFailed => Interlocked.Read(ref _etwCallbackEnqueueFailed);
    public long EtwCallbackEnqueueSucceeded => Interlocked.Read(ref _etwCallbackEnqueueSucceeded);
    public long EtwCallbackEventsReceived => Interlocked.Read(ref _etwCallbackEventsReceived);
    public long EtwCallbackEventsRejectedByNativeFilter => Interlocked.Read(ref _etwCallbackEventsRejectedByNativeFilter);
    public long EtwCallbackSelfProcessEventsDropped => Interlocked.Read(ref _etwCallbackSelfProcessEventsDropped);
    public long EtwEventsDroppedByBackpressure => Interlocked.Read(ref _etwEventsDroppedByBackpressure);
    public long EtwEventsDroppedByChannel => Interlocked.Read(ref _eventsDroppedByChannel);
    public long EtwEventsEmitted => Interlocked.Read(ref _etwEventsEmitted);
    public long EtwEventsEnqueued => Interlocked.Read(ref _eventsEnqueued);
    public long EtwEventsLostByEtw => Interlocked.Read(ref _eventsLostByEtw);
    public long EtwEventsParsed => Interlocked.Read(ref _eventsParsed);
    public long EtwEventsReceived => Interlocked.Read(ref _eventsReceived);
    public long EtwProjectedPayloadFieldDecodeFailures => Interlocked.Read(ref _etwProjectedPayloadFieldDecodeFailures);
    public long EtwProjectedPayloadFieldMissingDistinctKeys => Interlocked.Read(ref _etwProjectedPayloadFieldMissingDistinctKeys);
    public long EtwProjectedPayloadFieldMissingOccurrences => Interlocked.Read(ref _etwProjectedPayloadFieldMissingOccurrences);
    public long EtwWorkerEventsMaterialized => Interlocked.Read(ref _etwWorkerEventsMaterialized);
    public long EtwWorkerMaterializationFailures => Interlocked.Read(ref _etwWorkerMaterializationFailures);
    public long FileInfoClassKnown => Interlocked.Read(ref _fileInfoClassKnown);
    public long FileInfoClassUnknown => Interlocked.Read(ref _fileInfoClassUnknown);
    public long FileKeyResolverHits => Interlocked.Read(ref _fileKeyResolverHits);
    public long FileKeyResolverMisses => Interlocked.Read(ref _fileKeyResolverMisses);
    public long FileObjectResolverHits => Interlocked.Read(ref _fileObjectResolverHits);
    public long FileObjectResolverMisses => Interlocked.Read(ref _fileObjectResolverMisses);
    public long ForwarderFailures => Interlocked.Read(ref _forwarderFailures);
    public long IoWaitClassifiedEvents => Interlocked.Read(ref _ioWaitClassifiedEvents);
    public long IrpOperationsCompleted => Interlocked.Read(ref _irpOperationsCompleted);
    public long IrpOperationsMissingEnd => Interlocked.Read(ref _irpOperationsMissingEnd);
    public long IrpOperationsMissingStart => Interlocked.Read(ref _irpOperationsMissingStart);
    public long IrpOperationsStarted => Interlocked.Read(ref _irpOperationsStarted);
    public long IrpOperationsWithoutIrp => Interlocked.Read(ref _irpOperationsWithoutIrp);
    public long IrpReusedBeforeEnd => Interlocked.Read(ref _irpReusedBeforeEnd);
    public long IrpTrackerEvictions => Interlocked.Read(ref _irpTrackerEvictions);
    public long MismatchedSchedSwitchTids => Interlocked.Read(ref _mismatchedSchedSwitchTids);
    public long ParserFailures => Interlocked.Read(ref _parserFailures);
    public long ProcessResolverHits => Interlocked.Read(ref _processResolverHits);
    public long ProcessResolverMisses => Interlocked.Read(ref _processResolverMisses);
    public long ReadyThreadEventsReceived => Interlocked.Read(ref _readyThreadEventsReceived);
    public long RundownEventsReceived => Interlocked.Read(ref _rundownEventsReceived);
    public long SchedulerEventsDropped => Interlocked.Read(ref _schedulerEventsDropped);
    public long SchedulerEventsParsed => Interlocked.Read(ref _schedulerEventsParsed);
    public long SpoolBytes => Interlocked.Read(ref _spoolBytes);
    public long ThreadStateCacheHits => Interlocked.Read(ref _threadStateCacheHits);
    public long ThreadStateCacheMisses => Interlocked.Read(ref _threadStateCacheMisses);
    public long ThreadStateInvalid => Interlocked.Read(ref _threadStateInvalid);
    public long UnknownOpcodeEvents => Interlocked.Read(ref _unknownOpcodeEvents);
    public long UnknownProviderEvents => Interlocked.Read(ref _unknownProviderEvents);
    public long WaitReasonInvalid => Interlocked.Read(ref _waitReasonInvalid);

    public void AddEventsLostByEtw(long count) => Interlocked.Add(ref _eventsLostByEtw, count);

    public void AddSpoolBytes(long bytes) => Interlocked.Add(ref _spoolBytes, bytes);

    public void IncrementAnonymizedThreadEvents() => Interlocked.Increment(ref _anonymizedThreadEvents);

    public void IncrementCacheEvictions() => Interlocked.Increment(ref _cacheEvictions);

    public void IncrementCallbackExceptions() => Interlocked.Increment(ref _callbackExceptions);

    public void IncrementCswitchEventsReceived() => Interlocked.Increment(ref _cswitchEventsReceived);

    public void IncrementEtwCallbackEnqueueFailed() => Interlocked.Increment(ref _etwCallbackEnqueueFailed);

    public void IncrementEtwCallbackEnqueueSucceeded() => Interlocked.Increment(ref _etwCallbackEnqueueSucceeded);

    public void IncrementEtwCallbackEventsReceived() => Interlocked.Increment(ref _etwCallbackEventsReceived);

    public void IncrementEtwCallbackEventsRejectedByNativeFilter() => Interlocked.Increment(ref _etwCallbackEventsRejectedByNativeFilter);

    public void IncrementEtwCallbackSelfProcessEventsDropped() => Interlocked.Increment(ref _etwCallbackSelfProcessEventsDropped);

    public void IncrementEtwEventsDroppedByBackpressure() => Interlocked.Increment(ref _etwEventsDroppedByBackpressure);

    public void IncrementEtwEventsEmitted() => Interlocked.Increment(ref _etwEventsEmitted);

    public void IncrementEtwProjectedPayloadFieldDecodeFailures() => Interlocked.Increment(ref _etwProjectedPayloadFieldDecodeFailures);

    public void IncrementEtwProjectedPayloadFieldMissingDistinctKeys() => Interlocked.Increment(ref _etwProjectedPayloadFieldMissingDistinctKeys);

    public void IncrementEtwProjectedPayloadFieldMissingOccurrences() => Interlocked.Increment(ref _etwProjectedPayloadFieldMissingOccurrences);

    public void IncrementEtwWorkerEventsMaterialized() => Interlocked.Increment(ref _etwWorkerEventsMaterialized);

    public void IncrementEtwWorkerMaterializationFailures() => Interlocked.Increment(ref _etwWorkerMaterializationFailures);

    public void IncrementEventsDroppedByChannel() => Interlocked.Increment(ref _eventsDroppedByChannel);

    public void IncrementEventsEnqueued() => Interlocked.Increment(ref _eventsEnqueued);

    public void IncrementEventsParsed() => Interlocked.Increment(ref _eventsParsed);

    public void IncrementEventsReceived() => Interlocked.Increment(ref _eventsReceived);

    public void IncrementFileInfoClassKnown() => Interlocked.Increment(ref _fileInfoClassKnown);

    public void IncrementFileInfoClassUnknown() => Interlocked.Increment(ref _fileInfoClassUnknown);

    public void IncrementFileKeyResolverHits() => Interlocked.Increment(ref _fileKeyResolverHits);

    public void IncrementFileKeyResolverMisses() => Interlocked.Increment(ref _fileKeyResolverMisses);

    public void IncrementFileObjectResolverHits() => Interlocked.Increment(ref _fileObjectResolverHits);

    public void IncrementFileObjectResolverMisses() => Interlocked.Increment(ref _fileObjectResolverMisses);

    public void IncrementForwarderFailures() => Interlocked.Increment(ref _forwarderFailures);

    public void IncrementIoWaitClassifiedEvents() => Interlocked.Increment(ref _ioWaitClassifiedEvents);

    public void IncrementIrpOperationsCompleted() => Interlocked.Increment(ref _irpOperationsCompleted);

    public void IncrementIrpOperationsMissingEnd() => Interlocked.Increment(ref _irpOperationsMissingEnd);

    public void IncrementIrpOperationsMissingStart() => Interlocked.Increment(ref _irpOperationsMissingStart);

    public void IncrementIrpOperationsStarted() => Interlocked.Increment(ref _irpOperationsStarted);

    public void IncrementIrpOperationsWithoutIrp() => Interlocked.Increment(ref _irpOperationsWithoutIrp);

    public void IncrementIrpReusedBeforeEnd() => Interlocked.Increment(ref _irpReusedBeforeEnd);

    public void IncrementIrpTrackerEvictions() => Interlocked.Increment(ref _irpTrackerEvictions);

    public void IncrementMismatchedSchedSwitchTids() => Interlocked.Increment(ref _mismatchedSchedSwitchTids);

    public void IncrementParserFailures() => Interlocked.Increment(ref _parserFailures);

    public void IncrementProcessResolverHits() => Interlocked.Increment(ref _processResolverHits);

    public void IncrementProcessResolverMisses() => Interlocked.Increment(ref _processResolverMisses);

    public void IncrementReadyThreadEventsReceived() => Interlocked.Increment(ref _readyThreadEventsReceived);

    public void IncrementRundownEventsReceived() => Interlocked.Increment(ref _rundownEventsReceived);

    public void IncrementSchedulerEventsDropped() => Interlocked.Increment(ref _schedulerEventsDropped);

    public void IncrementSchedulerEventsParsed() => Interlocked.Increment(ref _schedulerEventsParsed);

    public void IncrementThreadStateCacheHits() => Interlocked.Increment(ref _threadStateCacheHits);

    public void IncrementThreadStateCacheMisses() => Interlocked.Increment(ref _threadStateCacheMisses);

    public void IncrementThreadStateInvalid() => Interlocked.Increment(ref _threadStateInvalid);

    public void IncrementUnknownOpcodeEvents() => Interlocked.Increment(ref _unknownOpcodeEvents);

    public void IncrementUnknownProviderEvents() => Interlocked.Increment(ref _unknownProviderEvents);

    public void IncrementWaitReasonInvalid() => Interlocked.Increment(ref _waitReasonInvalid);
}
