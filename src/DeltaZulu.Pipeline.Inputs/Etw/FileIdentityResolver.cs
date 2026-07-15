namespace DeltaZulu.Pipeline.Inputs.Etw;

public sealed class FileIdentityResolver
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ulong, FileIdentityState> _fileObjectMap = [];
    private readonly Dictionary<ulong, FileIdentityState> _fileKeyMap = [];
    private readonly TimeSpan _fileObjectTtl;
    private readonly TimeSpan _fileKeyTtl;
    private readonly TimeSpan _deletedTtl;
    private readonly int _maximumEntriesPerMap;

    public FileIdentityResolver(
        TimeSpan? fileObjectTtl = null,
        TimeSpan? fileKeyTtl = null,
        TimeSpan? deletedTtl = null,
        int maximumEntriesPerMap = 100_000)
    {
        if (maximumEntriesPerMap <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntriesPerMap), "Cache capacity must be positive.");
        }

        _fileObjectTtl = fileObjectTtl ?? TimeSpan.FromMinutes(10);
        _fileKeyTtl = fileKeyTtl ?? TimeSpan.FromMinutes(30);
        _deletedTtl = deletedTtl ?? TimeSpan.FromSeconds(30);
        _maximumEntriesPerMap = maximumEntriesPerMap;
    }

    public int FileObjectCount
    {
        get { lock (_gate)
            {
                return _fileObjectMap.Count;
            }
        }
    }

    public int FileKeyCount
    {
        get { lock (_gate)
            {
                return _fileKeyMap.Count;
            }
        }
    }

    public void ObserveNativePath(
        ulong? fileObject,
        ulong? fileKey,
        string path,
        DateTimeOffset timestampUtc,
        FileResolutionSource source = FileResolutionSource.NativePayload) =>
        ObservePath(fileObject, fileKey, path, timestampUtc, source, ResolutionConfidence.High);

    public void ObserveCreate(
        ulong fileObject,
        ulong? fileKey,
        string path,
        DateTimeOffset timestampUtc,
        FileResolutionSource source = FileResolutionSource.FileIoCreate) =>
        ObservePath(fileObject, fileKey, path, timestampUtc, source, ResolutionConfidence.High);

    public void ObserveName(
        ulong fileObject,
        ulong? fileKey,
        string path,
        DateTimeOffset timestampUtc,
        FileResolutionSource source = FileResolutionSource.FileIoName) =>
        ObservePath(fileObject, fileKey, path, timestampUtc, source, ResolutionConfidence.High);

    public void ObserveRundown(
        ulong fileObject,
        ulong? fileKey,
        string path,
        DateTimeOffset timestampUtc,
        FileResolutionSource source = FileResolutionSource.FileIoRundown) =>
        ObservePath(fileObject, fileKey, path, timestampUtc, source, ResolutionConfidence.Medium);

    public void ObserveDelete(ulong fileObject, DateTimeOffset timestampUtc)
    {
        lock (_gate)
        {
            PruneExpired(timestampUtc);

            if (!_fileObjectMap.TryGetValue(fileObject, out var state))
            {
                return;
            }

            var stale = state with {
                Source = FileResolutionSource.DelayedCache,
                Confidence = ResolutionConfidence.Low,
                LastSeenUtc = timestampUtc,
                Deleted = true
            };

            Store(stale, timestampUtc);
        }
    }

    public FileIdentityResolution Resolve(
        ulong? fileObject,
        ulong? fileKey,
        DateTimeOffset timestampUtc)
    {
        lock (_gate)
        {
            PruneExpired(timestampUtc);

            if (fileObject.HasValue && _fileObjectMap.TryGetValue(fileObject.Value, out var byObject))
            {
                return FileIdentityResolution.FromState(
                    byObject,
                    byObject.Deleted ? FileResolutionSource.DelayedCache : FileResolutionSource.FileObjectCache,
                    timestampUtc);
            }

            if (fileKey.HasValue && _fileKeyMap.TryGetValue(fileKey.Value, out var byKey))
            {
                return FileIdentityResolution.FromState(
                    byKey,
                    byKey.Deleted ? FileResolutionSource.DelayedCache : FileResolutionSource.FileKeyCache,
                    timestampUtc);
            }

            return FileIdentityResolution.Unresolved(new FileIdentityKey(fileObject, fileKey));
        }
    }

    private void ObservePath(
        ulong? fileObject,
        ulong? fileKey,
        string path,
        DateTimeOffset timestampUtc,
        FileResolutionSource source,
        ResolutionConfidence confidence)
    {
        if (!fileObject.HasValue && !fileKey.HasValue)
        {
            throw new ArgumentException("At least one file identity key is required.", nameof(fileObject));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A non-empty path is required.", nameof(path));
        }

        lock (_gate)
        {
            PruneExpired(timestampUtc);
            Store(new FileIdentityState(fileObject, fileKey, path, source, confidence, timestampUtc, timestampUtc), timestampUtc);
        }
    }

    private void Store(FileIdentityState state, DateTimeOffset timestampUtc)
    {
        if (state.FileObject.HasValue)
        {
            _fileObjectMap[state.FileObject.Value] = state;
            TrimToCapacity(_fileObjectMap);
        }

        if (state.FileKey.HasValue)
        {
            _fileKeyMap[state.FileKey.Value] = state;
            TrimToCapacity(_fileKeyMap);
        }
    }

    private void PruneExpired(DateTimeOffset nowUtc)
    {
        PruneExpired(_fileObjectMap, nowUtc, _fileObjectTtl);
        PruneExpired(_fileKeyMap, nowUtc, _fileKeyTtl);
    }

    private void PruneExpired(Dictionary<ulong, FileIdentityState> map, DateTimeOffset nowUtc, TimeSpan ttl)
    {
        foreach (var (key, state) in map.ToArray())
        {
            var effectiveTtl = state.Deleted ? _deletedTtl : ttl;
            if (nowUtc - state.LastSeenUtc > effectiveTtl)
            {
                map.Remove(key);
            }
        }
    }

    private void TrimToCapacity(Dictionary<ulong, FileIdentityState> map)
    {
        while (map.Count > _maximumEntriesPerMap)
        {
            var oldest = map.MinBy(entry => entry.Value.LastSeenUtc).Key;
            map.Remove(oldest);
        }
    }
}
