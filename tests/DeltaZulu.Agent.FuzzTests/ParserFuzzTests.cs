using DeltaZulu.Forward;
using DeltaZulu.Pipeline.Inputs.Auditd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DeltaZulu.Agent.FuzzTests;

[TestClass]
public sealed class ParserFuzzTests
{
    [TestMethod]
    public void AuditdRecordParser_DoesNotCrashOnUntrustedInput()
    {
        FuzzAuditd("type=PATH msg=audit(1710000000.123:42): item=0 name=\"/bin/bash\"");
    }

    [TestMethod]
    public void ForwardLogBatchCodec_DoesNotCrashOnUntrustedInput()
    {
        FuzzForwardBatch(CreateForwardSeed());
    }

    [TestMethod]
    public void GenerateForwardSeedCorpus()
    {
        var seed = CreateForwardSeed();
        var seedPath = Environment.GetEnvironmentVariable("DELTAZULU_FUZZ_SEED_PATH");
        if (string.IsNullOrWhiteSpace(seedPath))
        {
            var batch = ForwardLogBatchCodec.Decode(seed);
            Assert.AreEqual(Guid.Parse("8f614894-f861-43c9-a7e5-f06c42491e71"), batch.BatchId);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(seedPath)!);
        File.WriteAllBytes(seedPath, seed);
    }

    private static byte[] CreateForwardSeed() =>
        ForwardLogBatchCodec.Encode(new ForwardLogBatch {
            BatchId = Guid.Parse("8f614894-f861-43c9-a7e5-f06c42491e71"),
            CreatedAt = DateTimeOffset.Parse("2026-06-27T00:00:00Z"),
            Records = []
        }).ToArray();

    private static void FuzzAuditd(string line)
    {
        try
        {
            _ = new AuditdRecordParser().Parse(line);
        }
        catch (FormatException)
        {
            // Invalid audit records are an expected parser outcome.
        }
    }

    private static void FuzzForwardBatch(ReadOnlyMemory<byte> payload)
    {
        try
        {
            _ = ForwardLogBatchCodec.Decode(payload);
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException)
        {
            // Invalid wire payloads are expected; unexpected exception types remain fuzzing failures.
        }
    }
}
