namespace DeltaZulu.Pipeline.Core.Profiles;

public sealed class ResourceProfileValidator
{
    public void ThrowIfInvalid(ResourceProfile profile, string? source = null)
    {
        var errors = Validate(profile);
        if (errors.Count > 0)
        {
            var prefix = string.IsNullOrWhiteSpace(source) ? "Invalid resource profile" : $"Invalid resource profile '{source}'";
            throw new InvalidDataException(prefix + ": " + string.Join("; ", errors));
        }
    }

    public IReadOnlyList<string> Validate(ResourceProfile profile)
    {
        var errors = new List<string>();
        if (profile.SchemaVersion <= 0)
        {
            errors.Add("schemaVersion must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            errors.Add("id is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            errors.Add("name is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.Version))
        {
            errors.Add("version is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.Resource.Platform))
        {
            errors.Add("resource.platform is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.Resource.Family))
        {
            errors.Add("resource.family is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.Input.Table)
            && string.IsNullOrWhiteSpace(YamlResourceProfileLoader.DefaultInputTable(profile.Resource.Family)))
        {
            errors.Add("input.table is required when no default exists for resource.family.");
        }

        if (string.IsNullOrWhiteSpace(profile.Input.Schema)
            && string.IsNullOrWhiteSpace(YamlResourceProfileLoader.DefaultInputSchema(profile.Resource.Family)))
        {
            errors.Add("input.schema is required when no default exists for resource.family.");
        }

        if (!string.IsNullOrWhiteSpace(profile.Filter.Language)
            && !profile.Filter.Language.Equals("kql", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Only filter.language: kql is supported in this implementation.");
        }

        if (!profile.Output.Format.Equals("ndjson", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Only output.format: ndjson is supported in this implementation.");
        }

        if (!profile.Output.PreserveOriginalFieldNames)
        {
            errors.Add("preserveOriginalFieldNames must remain true. Server-side normalization owns semantic field mapping.");
        }

        if (profile.Condition is not null)
        {
            // Core validates shape only. Whether a given condition.type has a registered
            // evaluator on this host/platform is a pre-filter concern, not a schema concern.
            if (string.IsNullOrWhiteSpace(profile.Condition.Type))
            {
                errors.Add("condition.type is required when condition is specified.");
            }

            if (string.IsNullOrWhiteSpace(profile.Condition.Query))
            {
                errors.Add("condition.query is required when condition is specified.");
            }
        }

        return errors;
    }
}
