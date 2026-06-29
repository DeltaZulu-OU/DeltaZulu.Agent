using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace DeltaZulu.Agent.Security.EtwIntegrity;

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
        IntPtr ntdll = NativeMethods.GetModuleHandleW("ntdll.dll");

        if (ntdll == IntPtr.Zero)
        {
            throw new Win32Exception("Failed to resolve loaded ntdll.dll.");
        }

        string modulePath = GetModulePath(ntdll);
        int processId = Environment.ProcessId;
        string architecture = RuntimeInformation.ProcessArchitecture.ToString();
        var baselines = new List<EtwFunctionBaseline>();

        foreach (string functionName in targetFunctions)
        {
            IntPtr address = NativeMethods.GetProcAddress(ntdll, functionName);
            if (address == IntPtr.Zero)
            {
                continue;
            }

            MemoryReadResult read = _memoryReader.TryRead(address, prologueSize);
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
        int capacity = 260;

        while (capacity <= 32768)
        {
            var buffer = new StringBuilder(capacity);
            uint length = NativeMethods.GetModuleFileNameW(moduleHandle, buffer, buffer.Capacity);

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
