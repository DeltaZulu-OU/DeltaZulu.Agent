namespace DeltaZulu.Pipeline.Inputs.Windows;

internal static class EtwTdhEventFields
{
    public static IReadOnlyDictionary<string, object?> Materialize(IDictionary<string, object> fields)
    {
        var materialized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            materialized[field.Key] = field.Value;
        }

        return materialized.AsReadOnly();
    }
}
