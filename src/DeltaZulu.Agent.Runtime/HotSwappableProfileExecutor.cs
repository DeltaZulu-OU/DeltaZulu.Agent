using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Agent.Runtime;

internal sealed class HotSwappableProfileExecutor : IDisposable
{
    private readonly IProfileExecutor _executor;
    private readonly ProfileReloadSource _profiles;
    private readonly Lock _reloadGate = new();
    private ResourceProfile _currentProfile;
    private bool _disposed;

    public HotSwappableProfileExecutor(IProfileExecutor executor, ProfileReloadSource profiles)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _currentProfile = profiles.Current;
    }

    public IObservable<ResourceOutputRecord> Execute(IObservable<SourceEvent> source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Observable.Create<ResourceOutputRecord>(observer => {
            var events = new Subject<SourceEvent>();
            var activeProfile = new SerialDisposable();
            var stopped = false;

            void ActivateProfile(ResourceProfile profile)
            {
                lock (_reloadGate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (stopped)
                    {
                        return;
                    }

                    _currentProfile = profile;
                    activeProfile.Disposable = _executor.Execute(events, profile, cancellationToken).Subscribe(observer);
                }
            }

            void OnProfileChanged(object? sender, ResourceProfile profile) => ActivateProfile(profile);

            _profiles.ProfileChanged += OnProfileChanged;

            try
            {
                ActivateProfile(_profiles.Current);
            }
            catch (Exception ex)
            {
                _profiles.ProfileChanged -= OnProfileChanged;
                activeProfile.Dispose();
                events.Dispose();
                observer.OnError(ex);
                return Disposable.Empty;
            }

            var sourceSubscription = source.Subscribe(
                sourceEvent => {
                    lock (_reloadGate)
                    {
                        if (!stopped)
                        {
                            events.OnNext(sourceEvent);
                        }
                    }
                },
                error => {
                    lock (_reloadGate)
                    {
                        if (!stopped)
                        {
                            stopped = true;
                            events.OnError(error);
                        }
                    }
                },
                () => {
                    lock (_reloadGate)
                    {
                        if (!stopped)
                        {
                            stopped = true;
                            events.OnCompleted();
                        }
                    }
                });

            return Disposable.Create(() => {
                lock (_reloadGate)
                {
                    stopped = true;
                }

                _profiles.ProfileChanged -= OnProfileChanged;
                sourceSubscription.Dispose();
                activeProfile.Dispose();
                events.Dispose();
            });
        });
    }

    public void Dispose()
    {
        lock (_reloadGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }
    }
}
