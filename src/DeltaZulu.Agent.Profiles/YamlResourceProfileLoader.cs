using DeltaZulu.Agent.Application.Abstractions;
using DeltaZulu.Agent.Domain.Profiles;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DeltaZulu.Agent.Profiles;

public sealed class YamlResourceProfileLoader : IProfileRepository
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
        var profile = DeserializeFile(path);
        _validator.ThrowIfInvalid(profile, path);
        return profile;
    }

    public ProfileLoadResult LoadDirectory(string path, string searchPattern = "*.yaml")
    {
        var profiles = new List<ResourceProfile>();
        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var file in Directory.EnumerateFiles(path, searchPattern, SearchOption.AllDirectories))
        {
            try
            {
                var profile = DeserializeFile(file);
                var validationErrors = _validator.Validate(profile);
                if (validationErrors.Count > 0)
                {
                    AddProfileLoadIssue(profile, file, string.Join("; ", validationErrors), errors, warnings);
                    continue;
                }

                profiles.Add(profile);
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

        return new ProfileLoadResult(profiles, errors, warnings);
    }

    private ResourceProfile DeserializeFile(string path)
    {
        using var reader = File.OpenText(path);
        return _deserializer.Deserialize<ResourceProfile>(reader)
            ?? throw new InvalidDataException($"Profile file '{path}' did not contain a resource profile.");
    }

    private static void AddProfileLoadIssue(
        ResourceProfile profile,
        string file,
        string message,
        List<string> errors,
        List<string> warnings)
    {
        var issue = $"{file}: Invalid resource profile '{file}' ({profile.Id}): {message}";
        if (profile.Mandatory)
        {
            errors.Add(issue);
            return;
        }

        warnings.Add(issue);
    }
}
