using System.Dynamic;

namespace DeltaZulu.Pipeline.Core.Events;

public static class DictionaryCoercion
{
    public static IReadOnlyDictionary<string, object?>? CoerceToNullableDictionary(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is IReadOnlyDictionary<string, object?> ro)
        {
            return CopyDictionary(ro);
        }

        if (value is IDictionary<string, object?> dict)
        {
            return CopyDictionary(dict);
        }

        if (value is IDictionary<string, object> legacy)
        {
            return CopyLegacyDictionary(legacy);
        }

        if (value is ExpandoObject expando)
        {
            return CopyDictionary((IDictionary<string, object?>)expando);
        }

        return new Dictionary<string, object?> { ["value"] = value };
    }

    public static IDictionary<string, object> ToKqlDictionary(IDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var field in source)
        {
            if (field.Value is not null)
            {
                result[field.Key] = field.Value;
            }
        }

        return result;
    }

    public static Dictionary<string, object?> ToObjectDictionary(IReadOnlyDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object?>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var field in source)
        {
            result[field.Key] = field.Value;
        }

        return result;
    }

    public static Dictionary<string, object?> ToObjectDictionary(IDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object?>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var field in source)
        {
            result[field.Key] = field.Value;
        }

        return result;
    }

    private static Dictionary<string, object?> CopyDictionary(IReadOnlyDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object?>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var field in source)
        {
            result[field.Key] = field.Value;
        }

        return result;
    }

    private static Dictionary<string, object?> CopyDictionary(IDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object?>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var field in source)
        {
            result[field.Key] = field.Value;
        }

        return result;
    }

    private static Dictionary<string, object?> CopyLegacyDictionary(IDictionary<string, object> source)
    {
        var result = new Dictionary<string, object?>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var field in source)
        {
            result[field.Key] = field.Value;
        }

        return result;
    }
}
