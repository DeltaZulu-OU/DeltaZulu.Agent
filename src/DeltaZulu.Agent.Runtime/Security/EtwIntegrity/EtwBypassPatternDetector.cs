using System.Buffers.Binary;

namespace DeltaZulu.Agent.Runtime.Security.EtwIntegrity;

public static class EtwBypassPatternDetector
{
    public static EtwIntegrityDetectionResult Detect(
        ReadOnlySpan<byte> current,
        ReadOnlySpan<byte> baseline,
        string functionName,
        int prologueSize)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            functionName = "<unknown>";
        }

        if (prologueSize < 6)
        {
            return EtwIntegrityDetectionResult.Invalid("Prologue size must be at least 6 bytes.");
        }

        if (current.Length < prologueSize)
        {
            return EtwIntegrityDetectionResult.Invalid($"Current buffer too small. Required {prologueSize}, got {current.Length}.");
        }

        if (baseline.Length < prologueSize)
        {
            return EtwIntegrityDetectionResult.Invalid($"Baseline buffer too small. Required {prologueSize}, got {baseline.Length}.");
        }

        current = current[..prologueSize];
        baseline = baseline[..prologueSize];

        if (current[0] == 0xC3)
        {
            return EtwIntegrityDetectionResult.Detected(
                EtwIntegrityPattern.Ret,
                $"{functionName} patched with RET (0xC3).",
                changedOffset: 0,
                expectedByte: baseline[0],
                actualByte: current[0]);
        }

        if (StartsWith(current, 0x90, 0x90, 0x90, 0x90))
        {
            return EtwIntegrityDetectionResult.Detected(
                EtwIntegrityPattern.NopSled,
                $"{functionName} patched with NOP sled.",
                changedOffset: FirstChangedOffset(current, baseline),
                expectedByte: GetExpected(current, baseline),
                actualByte: GetActual(current, baseline));
        }

        if (StartsWith(current, 0x31, 0xC0, 0xC3) || StartsWith(current, 0x33, 0xC0, 0xC3))
        {
            return EtwIntegrityDetectionResult.Detected(
                EtwIntegrityPattern.XorEaxRet,
                $"{functionName} patched with XOR EAX,EAX; RET.",
                changedOffset: FirstChangedOffset(current, baseline),
                expectedByte: GetExpected(current, baseline),
                actualByte: GetActual(current, baseline));
        }

        if (current[0] == 0xB8 && current[5] == 0xC3)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(current.Slice(1, 4));

            return EtwIntegrityDetectionResult.Detected(
                EtwIntegrityPattern.MovEaxImmediateRet,
                $"{functionName} patched with MOV EAX, 0x{value:X8}; RET.",
                changedOffset: FirstChangedOffset(current, baseline),
                expectedByte: GetExpected(current, baseline),
                actualByte: GetActual(current, baseline),
                forcedReturnValue: value);
        }

        for (int i = 0; i < prologueSize; i++)
        {
            if (current[i] != baseline[i])
            {
                return EtwIntegrityDetectionResult.Detected(
                    EtwIntegrityPattern.GenericModification,
                    $"{functionName} modified at offset {i}: expected 0x{baseline[i]:X2}, found 0x{current[i]:X2}.",
                    changedOffset: i,
                    expectedByte: baseline[i],
                    actualByte: current[i]);
            }
        }

        return EtwIntegrityDetectionResult.Clean($"{functionName} prologue matches startup baseline.");
    }

    private static bool StartsWith(ReadOnlySpan<byte> buffer, params byte[] pattern) =>
        buffer.Length >= pattern.Length && buffer[..pattern.Length].SequenceEqual(pattern);

    private static int? FirstChangedOffset(ReadOnlySpan<byte> current, ReadOnlySpan<byte> baseline)
    {
        int length = Math.Min(current.Length, baseline.Length);

        for (int i = 0; i < length; i++)
        {
            if (current[i] != baseline[i])
            {
                return i;
            }
        }

        return null;
    }

    private static byte? GetExpected(ReadOnlySpan<byte> current, ReadOnlySpan<byte> baseline)
    {
        int? offset = FirstChangedOffset(current, baseline);
        return offset is null ? null : baseline[offset.Value];
    }

    private static byte? GetActual(ReadOnlySpan<byte> current, ReadOnlySpan<byte> baseline)
    {
        int? offset = FirstChangedOffset(current, baseline);
        return offset is null ? null : current[offset.Value];
    }
}
