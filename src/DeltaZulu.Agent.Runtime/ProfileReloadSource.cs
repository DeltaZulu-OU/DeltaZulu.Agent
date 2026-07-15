using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Agent.Runtime;

public sealed class ProfileReloadSource
{
    private readonly Lock _lock = new();
    private ResourceProfile _current;

    public ProfileReloadSource(ResourceProfile initialProfile)
    {
        _current = initialProfile ?? throw new ArgumentNullException(nameof(initialProfile));
    }

    public ResourceProfile Current {
        get {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public event EventHandler<ResourceProfile>? ProfileChanged;

    public void NotifyProfileChanged(ResourceProfile validatedProfile)
    {
        ArgumentNullException.ThrowIfNull(validatedProfile);
        lock (_lock)
        {
            _current = validatedProfile;
        }

        ProfileChanged?.Invoke(this, validatedProfile);
    }
}