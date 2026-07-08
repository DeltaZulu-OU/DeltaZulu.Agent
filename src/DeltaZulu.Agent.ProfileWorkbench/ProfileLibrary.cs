using DeltaZulu.Pipeline.Core.Profiles;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DeltaZulu.Agent.ProfileWorkbench;

public sealed record ProfileLibraryItem(
    string Id,
    string Name,
    string Path,
    string Platform,
    string Family,
    string Table,
    bool Enabled);

public sealed record ResourceProfileDocument(
    string Path,
    string RawText,
    ResourceProfile Profile)
{
    public string Query
    {
        get => Profile.Filter.Query;
        set => Profile.Filter.Query = value;
    }
}

public sealed record ProfileSaveResult(bool Success, string Path, string? Error = null);

public sealed class ProfileLibrary
{
    private readonly string _root;
    private readonly ProfileAvailabilityFilter _availabilityFilter;
    private readonly YamlResourceProfileLoader _loader = new();
    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public ProfileLibrary(string root, ProfileAvailabilityFilter? availabilityFilter = null)
    {
        _root = root;
        _availabilityFilter = availabilityFilter ?? new ProfileAvailabilityFilter();
    }

    public IReadOnlyList<ProfileLibraryItem> ListProfiles()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        var items = new List<ProfileLibraryItem>();
        foreach (var file in Directory.EnumerateFiles(_root, "*.yaml", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var profile = _loader.LoadFile(file);
                if (!_availabilityFilter.ShouldList(profile))
                {
                    continue;
                }

                items.Add(new ProfileLibraryItem(
                    profile.Id,
                    profile.Name,
                    file,
                    profile.Resource.Platform,
                    profile.Resource.Family,
                    profile.Input.Table,
                    profile.Enabled));
            }
            catch
            {
                // Invalid profiles are not listable. Profile validation diagnostics belong to
                // explicit validation/reporting flows, not to the selectable workbench catalog.
            }
        }

        return items;
    }

    public ResourceProfileDocument Open(ProfileLibraryItem item) => Open(item.Path);

    public ResourceProfileDocument Open(string path)
    {
        var raw = File.ReadAllText(path);
        var profile = _loader.LoadFile(path);
        return new ResourceProfileDocument(path, raw, profile);
    }

    public ProfileSaveResult Save(ResourceProfileDocument document)
    {
        try
        {
            var yaml = _serializer.Serialize(document.Profile);
            File.WriteAllText(document.Path, yaml);
            return new ProfileSaveResult(true, document.Path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return new ProfileSaveResult(false, document.Path, ex.GetBaseException().Message);
        }
    }
}
