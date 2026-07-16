namespace DeltaZulu.Pipeline.Core.Profiles;

/// <summary>
/// Typed accessors over <see cref="ResourceDescriptor.Options"/>. YamlDotNet deserializes an
/// open-ended YAML mapping into boxed scalars/lists (int, bool, string, List&lt;object&gt;); these
/// helpers unwrap that shape so family-specific option adapters do not each reimplement it.
/// </summary>
public static class ResourceOptionsAccessors
{
    public static bool GetBool(this IReadOnlyDictionary<string, object?> options, string key)
    {
        if (!options.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        return raw switch {
            bool value => value,
            string text => bool.TryParse(text, out var parsed) && parsed,
            _ => false
        };
    }

    public static List<int> GetIntList(this IReadOnlyDictionary<string, object?> options, string key)
    {
        if (!options.TryGetValue(key, out var raw) || raw is null)
        {
            return [];
        }

        if (raw is IEnumerable<object?> items)
        {
            return items.Where(item => item is not null).Select(item => Convert.ToInt32(item)).ToList();
        }

        return [Convert.ToInt32(raw)];
    }

    public static List<string> GetStringList(this IReadOnlyDictionary<string, object?> options, string key)
    {
        if (!options.TryGetValue(key, out var raw) || raw is null)
        {
            return [];
        }

        if (raw is IEnumerable<object?> items)
        {
            return items.Where(item => item is not null).Select(item => item!.ToString()!).ToList();
        }

        return [raw.ToString()!];
    }
}
