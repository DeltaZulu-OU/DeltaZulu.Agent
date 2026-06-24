using System.Dynamic;

namespace DeltaZulu.Agent.Core.Events;

public static class DictionaryCoercion
{
    public static Dictionary<string, object?> ToObjectDictionary(IReadOnlyDictionary<string, object?> source)
        => source.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, object?>? CoerceToNullableDictionary(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is IReadOnlyDictionary<string, object?> ro)
        {
            return new Dictionary<string, object?>(ro, StringComparer.OrdinalIgnoreCase);
        }

        if (value is IDictionary<string, object?> dict)
        {
            return new Dictionary<string, object?>(dict, StringComparer.OrdinalIgnoreCase);
        }

        if (value is IDictionary<string, object> legacy)
        {
            return legacy.ToDictionary(k => k.Key, v => (object?)v.Value, StringComparer.OrdinalIgnoreCase);
        }

        if (value is ExpandoObject expando)
        {
            return ((IDictionary<string, object?>)expando).ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, object?> { ["value"] = value };
    }

    public static IDictionary<string, object> ToKqlDictionary(IDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in source)
        {
            if (field.Value is not null)
            {
                result[field.Key] = field.Value;
            }
        }

        return result;
    }
}