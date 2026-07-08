namespace DeltaZulu.Agent.Runtime.Security.EtwIntegrity;

public interface IProcessMemoryReader
{
    MemoryReadResult TryRead(IntPtr address, int byteCount);
}

public sealed record MemoryReadResult(
    bool Success,
    byte[] Bytes,
    string? Error)
{
    public static MemoryReadResult Ok(byte[] bytes) => new(true, bytes, null);

    public static MemoryReadResult Fail(string error) => new(false, Array.Empty<byte>(), error);
}
