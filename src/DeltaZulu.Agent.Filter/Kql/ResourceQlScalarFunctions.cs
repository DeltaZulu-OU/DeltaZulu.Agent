using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Reactive.Kql;
using System.Runtime.Caching;

namespace DeltaZulu.Agent.Filter.Kql;

public static class AgentScalarFunctions
{
    private static readonly MemoryCache ProcessNameCache = new("AgentProcessNameCache");

    [KqlScalarFunction("strcat_array")]
    public static string StrCatArray(string[] values, string delimiter) => string.Join(delimiter, values);

    [KqlScalarFunction("strcat_array")]
    public static string StrCatArray(object?[] values, string delimiter) => string.Join(delimiter, values.Select(value => value?.ToString() ?? string.Empty));

    [KqlScalarFunction("strcat_array")]
    public static string StrCatArray(object? values, string delimiter)
    {
        if (values is null)
        {
            return string.Empty;
        }

        if (values is string text)
        {
            return text;
        }

        if (values is IEnumerable enumerable)
        {
            return string.Join(delimiter, enumerable.Cast<object?>().Select(value => value?.ToString() ?? string.Empty));
        }

        return values.ToString() ?? string.Empty;
    }

    [KqlScalarFunction("isnotempty")]
    public static bool IsNotEmpty(string? value) => !string.IsNullOrEmpty(value);

    [KqlScalarFunction("isnotempty")]
    public static bool IsNotEmpty(object? value) => !string.IsNullOrEmpty(value?.ToString());

    [KqlScalarFunction("ntohs")]
    public static int NetworkToHostPort(ushort port) =>
        (ushort)IPAddress.NetworkToHostOrder((short)port);

    [KqlScalarFunction("getprocessname")]
    public static string GetProcessName(uint pid)
    {
        var key = pid.ToString();
        if (ProcessNameCache.Contains(key))
        {
            return ProcessNameCache.Get(key)?.ToString() ?? string.Empty;
        }

        string result = string.Empty;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            result = process.ProcessName;
        }
        catch
        {
            result = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(result))
        {
            ProcessNameCache.Set(key, result, DateTimeOffset.UtcNow.AddSeconds(10));
        }

        return result;
    }
}