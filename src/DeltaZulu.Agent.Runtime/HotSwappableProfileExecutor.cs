using System.Reactive.Linq;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Agent.Runtime;

internal sealed class HotSwappableProfileExecutor : IDisposable
{
    private readonly IProfileExecutor _executor;
    private readonly ProfileReloadSource _profiles;
    private readonly object _reloadGate = new();
    private readonly ManualResetEventSlim _drained = new(true);
    private ResourceProfile _currentProfile;
    private int _inFlight;
    private bool _disposed;

    public HotSwappableProfileExecutor(IProfileExecutor executor, ProfileReloadSource profiles)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _currentProfile = profiles.Current;
        _profiles.ProfileChanged += OnProfileChanged;
    }

    public IObservable<ResourceOutputRecord> Execute(IObservable<SourceEvent> source, CancellationToken cancellationToken = default) =>
        source.SelectMany(sourceEvent => ExecuteOne(sourceEvent, cancellationToken));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _profiles.ProfileChanged -= OnProfileChanged;
        _drained.Dispose();
    }

    private IObservable<ResourceOutputRecord> ExecuteOne(SourceEvent sourceEvent, CancellationToken cancellationToken)
    {
        ResourceProfile profile;
        lock (_reloadGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            profile = _currentProfile;
            if (Interlocked.Increment(ref _inFlight) == 1)
            {
                _drained.Reset();
            }
        }

        try
        {
            return _executor
                .Execute(Observable.Return(sourceEvent), profile, cancellationToken)
                .Finally(MarkCompleted);
        }
        catch (Exception ex)
        {
            MarkCompleted();
            return Observable.Throw<ResourceOutputRecord>(ex);
        }
    }

    private void OnProfileChanged(object? sender, ResourceProfile profile)
    {
        ReplaceProfile(profile);
    }

    private void ReplaceProfile(ResourceProfile profile)
    {
        PauseAndDrain();
        try
        {
            _currentProfile = profile;
        }
        finally
        {
            Resume();
        }
    }

    private void PauseAndDrain()
    {
        Monitor.Enter(_reloadGate);
        _drained.Wait();
    }

    private void Resume() => Monitor.Exit(_reloadGate);

    private void MarkCompleted()
    {
        if (Interlocked.Decrement(ref _inFlight) == 0)
        {
            _drained.Set();
        }
    }
}