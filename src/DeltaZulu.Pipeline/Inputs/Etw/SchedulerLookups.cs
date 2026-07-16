namespace DeltaZulu.Pipeline.Inputs.Etw;

public static class ThreadStateLookup
{
    private static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string> {
        [0x00] = "Initialized",
        [0x01] = "Ready",
        [0x02] = "Running",
        [0x03] = "Standby",
        [0x04] = "Terminated",
        [0x05] = "Waiting",
        [0x06] = "Transition",
        [0x07] = "DeferredReady"
    };

    public static string? GetName(int state) => Names.GetValueOrDefault(state);
}

public static class WaitReasonLookup
{
    private static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string> {
        [0x02] = "PageIn",
        [0x07] = "WrExecutive",
        [0x08] = "WrFreePage",
        [0x09] = "WrPageIn",
        [0x0A] = "WrPoolAllocation",
        [0x0B] = "WrDelayExecution",
        [0x0C] = "WrSuspended",
        [0x0D] = "WrUserRequest",
        [0x0E] = "WrEventPair",
        [0x0F] = "WrQueue",
        [0x10] = "WrLpcReceive",
        [0x11] = "WrLpcReply",
        [0x12] = "WrVirtualMemory",
        [0x13] = "WrPageOut",
        [0x14] = "WrRendezvous",
        [0x15] = "WrKeyedEvent",
        [0x16] = "WrTerminated",
        [0x17] = "WrProcessInSwap",
        [0x18] = "WrCpuRateControl",
        [0x19] = "WrCalloutStack",
        [0x1A] = "WrKernel",
        [0x1B] = "WrResource",
        [0x1C] = "WrPushLock",
        [0x1D] = "WrMutex",
        [0x1E] = "WrQuantumEnd",
        [0x1F] = "WrDispatchInt",
        [0x20] = "WrPreempted",
        [0x21] = "WrYieldExecution",
        [0x22] = "WrFastMutex",
        [0x23] = "WrGuardedMutex",
        [0x24] = "WrRundown"
    };

    public static ThreadWaitClassification Classify(int? waitReason)
    {
        if (!waitReason.HasValue)
        {
            return new ThreadWaitClassification(null, null, ThreadWaitCategory.Unknown, false, "CSwitch");
        }

        var reason = waitReason.Value;
        var name = GetName(reason);
        var category = reason switch {
            0x02 or >= 0x07 and <= 0x0D or 0x10 or 0x11 or 0x24 => ThreadWaitCategory.IoWait,
            0x0B or 0x0C or 0x1E or 0x20 or 0x21 => ThreadWaitCategory.ExecutionDelay,
            0x0E or 0x0F or 0x14 or 0x15 or 0x1B or 0x1C or 0x1D or 0x22 or 0x23 => ThreadWaitCategory.Synchronization,
            0x08 or 0x0A or 0x12 or 0x13 => ThreadWaitCategory.Memory,
            _ => ThreadWaitCategory.Unknown
        };

        return new ThreadWaitClassification(reason, name, category, category == ThreadWaitCategory.IoWait, "CSwitch");
    }

    public static string? GetName(int waitReason) => Names.GetValueOrDefault(waitReason);
}
