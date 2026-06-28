using System.Management;
using System.Runtime.InteropServices;

namespace DeltaZulu.Pipeline.Inputs.Windows;

public static class WmiCondition
{
    public static bool Exists(string wqlQuery, string scopePath = @"\\.\root\cimv2")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wqlQuery);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopePath);

        var scope = new ManagementScope(scopePath);
        var query = new ObjectQuery(wqlQuery);
        using var searcher = new ManagementObjectSearcher(scope, query);
        using var results = searcher.Get();
        return results.Count > 0;
    }

    public static bool TryExists(
        string wqlQuery,
        out bool result,
        out Exception? error,
        string scopePath = @"\\.\root\cimv2")
    {
        try
        {
            result = Exists(wqlQuery, scopePath);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            result = false;
            error = ex;
            return false;
        }
    }
}
