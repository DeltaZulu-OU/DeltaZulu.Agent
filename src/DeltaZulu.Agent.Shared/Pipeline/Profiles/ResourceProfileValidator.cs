namespace DeltaZulu.Agent.Shared.Pipeline.Profiles;

public sealed class ResourceProfileValidator
{
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

        if (string.IsNullOrWhiteSpace(profile.Input.Table))
        {
            errors.Add("input.table is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.Input.Schema))
        {
            errors.Add("input.schema is required.");
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
            if (string.IsNullOrWhiteSpace(profile.Condition.Type))
            {
                errors.Add("condition.type is required when condition is specified.");
            }
            else if (!profile.Condition.Type.Equals("wmi", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Only condition.type: wmi is supported in this implementation.");
            }

            if (string.IsNullOrWhiteSpace(profile.Condition.Query))
            {
                errors.Add("condition.query is required when condition is specified.");
            }
        }

        return errors;
    }

    public void ThrowIfInvalid(ResourceProfile profile, string? source = null)
    {
        var errors = Validate(profile);
        if (errors.Count > 0)
        {
            var prefix = string.IsNullOrWhiteSpace(source) ? "Invalid resource profile" : $"Invalid resource profile '{source}'";
            throw new InvalidDataException(prefix + ": " + string.Join("; ", errors));
        }
    }
}
