using System.Diagnostics;
using System.Net;
using System.Reactive.Kql;
using System.Runtime.Caching;

namespace DeltaZulu.Pipeline.Kql;

public static class AgentScalarFunctions
{
    private static readonly MemoryCache ProcessNameCache = new("AgentProcessNameCache");

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