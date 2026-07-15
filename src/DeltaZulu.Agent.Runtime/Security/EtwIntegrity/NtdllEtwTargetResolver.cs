using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace DeltaZulu.Agent.Runtime.Security.EtwIntegrity;

public sealed class NtdllEtwTargetResolver
{
    private readonly IProcessMemoryReader _memoryReader;

    public NtdllEtwTargetResolver(IProcessMemoryReader memoryReader)
    {
        _memoryReader = memoryReader;
    }

    public IReadOnlyList<EtwFunctionBaseline> ResolveAndBaseline(
        IReadOnlyList<string> targetFunctions,
        int prologueSize)
    {
        var ntdll = NativeMethods.GetModuleHandleW("ntdll.dll");

        if (ntdll == IntPtr.Zero)
        {
            throw new Win32Exception("Failed to resolve loaded ntdll.dll.");
        }

        var modulePath = GetModulePath(ntdll);
        var processId = Environment.ProcessId;
        var architecture = RuntimeInformation.ProcessArchitecture.ToString();
        var baselines = new List<EtwFunctionBaseline>();

        foreach (var functionName in targetFunctions)
        {
            var address = NativeMethods.GetProcAddress(ntdll, functionName);
            if (address == IntPtr.Zero)
            {
                continue;
            }

            var read = _memoryReader.TryRead(address, prologueSize);
            if (!read.Success)
            {
                continue;
            }

            baselines.Add(new EtwFunctionBaseline(
                "ntdll.dll",
                functionName,
                modulePath,
                address,
                read.Bytes,
                Convert.ToHexString(SHA256.HashData(read.Bytes)),
                EtwBaselineSource.LiveProcessStartup,
                DateTimeOffset.UtcNow,
                processId,
                architecture));
        }

        return baselines;
    }

    private static string GetModulePath(IntPtr moduleHandle)
    {
        var capacity = 260;

        while (capacity <= 32768)
        {
            var buffer = new StringBuilder(capacity);
            var length = NativeMethods.GetModuleFileNameW(moduleHandle, buffer, buffer.Capacity);

            if (length == 0)
            {
                throw new Win32Exception("GetModuleFileNameW failed.");
            }

            if (length < buffer.Capacity - 1)
            {
                return buffer.ToString();
            }

            capacity *= 2;
        }

        throw new InvalidOperationException("Module path is too long.");
    }
}
