namespace DeltaZulu.Pipeline.Inputs.Windows;

internal static class EtwTdhEventFields
{
    public static bool TryMaterialize(
        IDictionary<string, object> fields,
        out IReadOnlyDictionary<string, object?> materialized,
        Action<Exception>? onDropped = null)
    {
        try
        {
            materialized = Materialize(fields);
            return true;
        }
        catch (Exception ex) when (IsUnmaterializableTdhEvent(ex))
        {
            onDropped?.Invoke(ex);
            materialized = new Dictionary<string, object?>(0, StringComparer.OrdinalIgnoreCase);
            return false;
        }
    }

    private static IReadOnlyDictionary<string, object?> Materialize(IDictionary<string, object> fields)
    {
        var materialized = new Dictionary<string, object?>(fields.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            materialized[field.Key] = field.Value;
        }

        return materialized;
    }

    private static bool IsUnmaterializableTdhEvent(Exception exception) =>
        exception is FormatException || IsTdhProviderNotFound(exception);

    private static bool IsTdhProviderNotFound(Exception exception)
    {
        const int ErrorNotFound = 1168;
        return exception.Message.Contains($"TDH status {ErrorNotFound}", StringComparison.OrdinalIgnoreCase);
    }
}
