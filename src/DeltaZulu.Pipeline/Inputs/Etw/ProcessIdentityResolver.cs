namespace DeltaZulu.Pipeline.Inputs.Etw;

public sealed class ProcessIdentityResolver
{
    private readonly Dictionary<ProcessIdentityKey, ProcessIdentityState> _byGeneration = [];
    private readonly Lock _gate = new();
    private readonly Dictionary<int, ProcessIdentityState> _latestByPid = [];
    private readonly int _maximumEntries;
    private readonly TimeSpan _ttl;

    public ProcessIdentityResolver(TimeSpan? ttl = null, int maximumEntries = 100_000)
    {
        if (maximumEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries), "Cache capacity must be positive.");
        }

        _ttl = ttl ?? TimeSpan.FromHours(2);
        _maximumEntries = maximumEntries;
    }

    public void ObserveProcess(
        int processId,
        DateTimeOffset? startTimeUtc,
        int? parentProcessId,
        string? imagePath,
        string? commandLine,
        string? userSid,
        DateTimeOffset observedAtUtc,
        ResolutionConfidence confidence = ResolutionConfidence.High)
    {
        if (processId <= 0)
        {
            return;
        }

        lock (_gate)
        {
            PruneExpired(observedAtUtc);
            var key = new ProcessIdentityKey(processId, startTimeUtc);
            var state = new ProcessIdentityState(
                key,
                parentProcessId,
                imagePath,
                commandLine,
                userSid,
                observedAtUtc,
                observedAtUtc,
                confidence);

            _latestByPid[processId] = state;
            _byGeneration[key] = state;
            TrimToCapacity();
        }
    }

    public ProcessIdentityResolution Resolve(
        int processId,
        DateTimeOffset? startTimeUtc,
        DateTimeOffset observedAtUtc)
    {
        lock (_gate)
        {
            PruneExpired(observedAtUtc);
            ProcessIdentityState? state = null;

            if (startTimeUtc.HasValue)
            {
                _byGeneration.TryGetValue(new ProcessIdentityKey(processId, startTimeUtc), out state);
            }

            if (state is null)
            {
                _latestByPid.TryGetValue(processId, out state);
            }

            if (state is null)
            {
                return new ProcessIdentityResolution(
                    null,
                    null,
                    "Unknown",
                    ResolutionConfidence.Unknown,
                    null,
                    new ProcessIdentityKey(processId, startTimeUtc).ToString());
            }

            return new ProcessIdentityResolution(
                state.ImagePath,
                state.CommandLine,
                startTimeUtc.HasValue ? "ProcessGenerationCache" : "ProcessIdCache",
                state.Confidence,
                Math.Max(0, (long)(observedAtUtc - state.LastSeenUtc).TotalMilliseconds),
                state.Key.ToString());
        }
    }

    private void PruneExpired(DateTimeOffset nowUtc)
    {
        foreach (var (pid, state) in _latestByPid.ToArray())
        {
            if (nowUtc - state.LastSeenUtc > _ttl)
            {
                _latestByPid.Remove(pid);
            }
        }

        foreach (var (key, state) in _byGeneration.ToArray())
        {
            if (nowUtc - state.LastSeenUtc > _ttl)
            {
                _byGeneration.Remove(key);
            }
        }
    }

    private void TrimToCapacity()
    {
        while (_byGeneration.Count > _maximumEntries)
        {
            var oldest = _byGeneration.MinBy(entry => entry.Value.LastSeenUtc);
            _byGeneration.Remove(oldest.Key);
            if (_latestByPid.TryGetValue(oldest.Value.Key.ProcessId, out var latest) && latest.Key == oldest.Key)
            {
                _latestByPid.Remove(oldest.Value.Key.ProcessId);
            }
        }
    }
}
