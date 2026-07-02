using System.Management;
using System.Runtime.InteropServices;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Platform.Prefilter.Windows;

/// <summary>
/// Evaluates <c>condition.type: wmi</c> by running a WQL existence query against the configured
/// (or default) WMI scope. Ported from the former DeltaZulu.Pipeline.Inputs.Windows.WmiCondition.
/// </summary>
public sealed class WmiConditionEvaluator : IResourceConditionEvaluator
{
    private const string DefaultScopePath = @"\\.\root\cimv2";

    public bool Handles(string conditionType) =>
        conditionType.Equals("wmi", StringComparison.OrdinalIgnoreCase);

    public bool TryEvaluate(ResourceCondition condition, out bool isSatisfied, out Exception? error)
    {
        var scopePath = string.IsNullOrWhiteSpace(condition.ScopePath) ? DefaultScopePath : condition.ScopePath;

        try
        {
            isSatisfied = Exists(condition.Query, scopePath);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            isSatisfied = false;
            error = ex;
            return false;
        }
    }

    private static bool Exists(string wqlQuery, string scopePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wqlQuery);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopePath);

        var scope = new ManagementScope(scopePath);
        var query = new ObjectQuery(wqlQuery);
        using var searcher = new ManagementObjectSearcher(scope, query);
        using var results = searcher.Get();
        return results.Count > 0;
    }
}
