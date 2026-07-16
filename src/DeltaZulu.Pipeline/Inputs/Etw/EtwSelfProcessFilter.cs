using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace DeltaZulu.Pipeline.Inputs.Etw;

public static class EtwSelfProcessFilter
{
    private static readonly ConcurrentDictionary<int, bool> AgentProcessIdCache = new();
    private static readonly Lazy<IReadOnlySet<string>> AgentProcessNames = new(CreateAgentProcessNames);
    public static IReadOnlySet<string> CurrentAgentProcessNames => AgentProcessNames.Value;
    public static int CurrentProcessId { get; } = Environment.ProcessId;

    public static bool IsSelfProcessEvent(int processId, string? processName = null) =>
        IsCurrentProcessId(processId) || IsAgentProcessName(processName) || IsAgentProcessId(processId);

    private static void AddAssemblyName(ISet<string> names, Assembly? assembly)
    {
        var name = assembly?.GetName().Name;
        if (!string.IsNullOrWhiteSpace(name))
        {
            names.Add(name);
        }
    }

    private static IReadOnlySet<string> CreateAgentProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddAssemblyName(names, Assembly.GetEntryAssembly());
        return names;
    }

    private static bool IsAgentProcessId(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        if (AgentProcessIdCache.TryGetValue(processId, out var isAgentProcess))
        {
            return isAgentProcess;
        }

        if (!ResolveAgentProcessId(processId))
        {
            return false;
        }

        AgentProcessIdCache[processId] = true;
        return true;
    }

    private static bool IsAgentProcessName(string? processName) =>
        !string.IsNullOrWhiteSpace(processName) && AgentProcessNames.Value.Contains(processName);

    private static bool IsCurrentProcessId(int processId) =>
                        processId > 0 && processId == CurrentProcessId;

    private static bool ResolveAgentProcessId(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return IsAgentProcessName(process.ProcessName);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
