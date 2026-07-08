using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DeltaZulu.Agent.Runtime.Security.EtwIntegrity;

public sealed class CurrentProcessMemoryReader : IProcessMemoryReader
{
    public MemoryReadResult TryRead(IntPtr address, int byteCount)
    {
        if (address == IntPtr.Zero)
        {
            return MemoryReadResult.Fail("Address is null.");
        }

        if (byteCount <= 0)
        {
            return MemoryReadResult.Fail("Byte count must be greater than zero.");
        }

        if (!IsReadableRange(address, byteCount, out string? validationError))
        {
            return MemoryReadResult.Fail(validationError ?? "Memory range is not readable.");
        }

        byte[] buffer = new byte[byteCount];

        bool ok = NativeMethods.ReadProcessMemory(
            NativeMethods.GetCurrentProcess(),
            address,
            buffer,
            new UIntPtr((uint)byteCount),
            out UIntPtr bytesRead);

        if (!ok)
        {
            return MemoryReadResult.Fail($"ReadProcessMemory failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }

        if (bytesRead.ToUInt64() != (ulong)byteCount)
        {
            return MemoryReadResult.Fail($"Partial read. Expected {byteCount}, got {bytesRead.ToUInt64()}.");
        }

        return MemoryReadResult.Ok(buffer);
    }

    private static bool IsReadableRange(IntPtr address, int byteCount, out string? error)
    {
        error = null;

        UIntPtr result = NativeMethods.VirtualQuery(
            address,
            out var mbi,
            new UIntPtr((uint)Marshal.SizeOf<NativeMethods.MEMORY_BASIC_INFORMATION>()));

        if (result == UIntPtr.Zero)
        {
            error = $"VirtualQuery failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
            return false;
        }

        if (mbi.State != NativeMethods.MEM_COMMIT)
        {
            error = "Memory region is not committed.";
            return false;
        }

        if ((mbi.Protect & NativeMethods.PAGE_GUARD) != 0)
        {
            error = "Memory region is guarded.";
            return false;
        }

        if ((mbi.Protect & NativeMethods.PAGE_NOACCESS) != 0)
        {
            error = "Memory region is no-access.";
            return false;
        }

        if (!IsPotentiallyReadableProtection(mbi.Protect))
        {
            error = $"Memory protection 0x{mbi.Protect:X} is not readable.";
            return false;
        }

        ulong start = unchecked((ulong)address.ToInt64());
        ulong regionStart = unchecked((ulong)mbi.BaseAddress.ToInt64());
        ulong regionSize = mbi.RegionSize.ToUInt64();
        ulong regionEnd = regionStart + regionSize;
        ulong requestedEnd = start + (ulong)byteCount;

        if (regionEnd < regionStart || requestedEnd < start)
        {
            error = "Requested memory range overflowed.";
            return false;
        }

        if (start < regionStart || requestedEnd > regionEnd)
        {
            error = "Requested range crosses the queried memory region boundary.";
            return false;
        }

        return true;
    }

    private static bool IsPotentiallyReadableProtection(uint protect)
    {
        uint normalized = protect & 0xFF;

        return normalized is
            NativeMethods.PAGE_READONLY or
            NativeMethods.PAGE_READWRITE or
            NativeMethods.PAGE_WRITECOPY or
            NativeMethods.PAGE_EXECUTE or
            NativeMethods.PAGE_EXECUTE_READ or
            NativeMethods.PAGE_EXECUTE_READWRITE or
            NativeMethods.PAGE_EXECUTE_WRITECOPY;
    }
}
