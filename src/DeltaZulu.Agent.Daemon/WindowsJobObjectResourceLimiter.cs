#if WINDOWS
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DeltaZulu.Agent.Daemon;

internal static partial class WindowsJobObjectResourceLimiter
{
    private const uint JobObjectCpuRateControlInformationClass = 15;
    private const uint JobObjectCpuRateControlEnable = 0x00000001;
    private const uint JobObjectCpuRateControlHardCap = 0x00000004;
    private const uint MinimumCpuRate = 1;
    private const uint MaximumCpuRate = 10_000;

    public static IDisposable ApplyToCurrentProcess(int cpuQuotaPercent)
    {
        if (cpuQuotaPercent is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(cpuQuotaPercent), cpuQuotaPercent, "CPU quota percent must be between 1 and 100.");
        }

        var jobHandle = NativeMethods.CreateJobObjectW(nint.Zero, null);
        if (jobHandle.IsInvalid)
        {
            throw CreateWin32Exception("create Windows job object");
        }

        try
        {
            var cpuRate = checked((uint)cpuQuotaPercent * 100);
            cpuRate = Math.Clamp(cpuRate, MinimumCpuRate, MaximumCpuRate);
            var cpuInfo = new JobObjectCpuRateControlInformation
            {
                ControlFlags = JobObjectCpuRateControlEnable | JobObjectCpuRateControlHardCap,
                CpuRate = cpuRate
            };

            if (!NativeMethods.SetInformationJobObject(
                    jobHandle,
                    JobObjectCpuRateControlInformationClass,
                    ref cpuInfo,
                    (uint)Marshal.SizeOf<JobObjectCpuRateControlInformation>()))
            {
                throw CreateWin32Exception("set Windows job object CPU quota");
            }

            if (!NativeMethods.AssignProcessToJobObject(jobHandle, Process.GetCurrentProcess().SafeHandle))
            {
                throw CreateWin32Exception("assign current process to Windows job object");
            }

            return jobHandle;
        }
        catch
        {
            jobHandle.Dispose();
            throw;
        }
    }

    private static Win32Exception CreateWin32Exception(string operation)
    {
        var error = Marshal.GetLastPInvokeError();
        return new Win32Exception(error, $"Failed to {operation}: {new Win32Exception(error).Message}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectCpuRateControlInformation
    {
        public uint ControlFlags;
        public uint CpuRate;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial SafeFileHandle CreateJobObjectW(nint lpJobAttributes, string? name);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetInformationJobObject(
            SafeFileHandle job,
            uint jobObjectInfoClass,
            ref JobObjectCpuRateControlInformation jobObjectInfo,
            uint jobObjectInfoLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
    }
}
#endif
