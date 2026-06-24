using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DeltaZulu.Agent.Profiles;

public sealed class YamlResourceProfileLoader
{
    private readonly IDeserializer _deserializer;
    private readonly ResourceProfileValidator _validator = new();

    public YamlResourceProfileLoader()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public ResourceProfile LoadFile(string path)
    {
        using var reader = File.OpenText(path);
        var profile = _deserializer.Deserialize<ResourceProfile>(reader)
            ?? throw new InvalidDataException($"Profile file '{path}' did not contain a resource profile.");

        _validator.ThrowIfInvalid(profile, path);
        return profile;
    }

    public ProfileLoadResult LoadDirectory(string path, string searchPattern = "*.yaml")
    {
        var profiles = new List<ResourceProfile>();
        var errors = new List<string>();

        foreach (var file in Directory.EnumerateFiles(path, searchPattern, SearchOption.AllDirectories))
        {
            try
            {
                profiles.Add(LoadFile(file));
            }
            catch (Exception ex)
            {
                errors.Add($"{file}: {ex.Message}");
            }
        }

        var duplicateIds = profiles
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var duplicate in duplicateIds)
        {
            errors.Add($"Duplicate profile id: {duplicate}");
        }

        return new ProfileLoadResult(profiles, errors);
    }
}