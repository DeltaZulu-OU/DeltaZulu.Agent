using System.Diagnostics;
using System.Net;
using System.Reactive.Kql;
using System.Runtime.Caching;

namespace DeltaZulu.Agent.Kql;

public static class AgentScalarFunctions
{
    private static readonly MemoryCache ProcessNameCache = new("AgentProcessNameCache");

    [KqlScalarFunction("ntohs")]
    public static int NetworkToHostPort(ushort port)
    {
        short converted = IPAddress.NetworkToHostOrder((short)port);
        var binary = Convert.ToString(converted, 2).PadLeft(8, '0');
        return Convert.ToInt32(binary, 2);
    }

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