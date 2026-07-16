namespace DeltaZulu.Pipeline.Inputs.Windows;

internal sealed class EtwTdhDropWarningLimiter
{
    private const int MaxDetailedWarnings = 3;
    private readonly Action<string>? _warn;
    private readonly string _sourceDescription;
    private readonly Dictionary<string, int> _countsByReason = new(StringComparer.OrdinalIgnoreCase);

    public EtwTdhDropWarningLimiter(Action<string>? warn, string sourceDescription)
    {
        _warn = warn;
        _sourceDescription = sourceDescription;
    }

    public void OnEventDropped(Exception ex)
    {
        if (_warn is null)
        {
            return;
        }

        var reason = string.IsNullOrWhiteSpace(ex.Message)
            ? ex.GetType().Name
            : ex.Message;

        _countsByReason.TryGetValue(reason, out var count);
        count++;
        _countsByReason[reason] = count;

        if (count <= MaxDetailedWarnings)
        {
            _warn($"{_sourceDescription}: event dropped (TDH materialization failed): {reason}");
            return;
        }

        if (count == MaxDetailedWarnings + 1)
        {
            _warn($"{_sourceDescription}: suppressing repeated TDH materialization failures for: {reason}");
        }
    }
}
