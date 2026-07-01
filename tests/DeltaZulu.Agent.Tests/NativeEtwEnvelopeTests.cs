using DeltaZulu.Pipeline.Core.Etw;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class NativeEtwEnvelopeTests
{
    [TestMethod]
    public void NativeEtwIdentityFilter_MatchesProviderOpcodeVersionAndKeywords()
    {
        var envelope = CreateEnvelope(opcode: 67, version: 3, keywords: 0x10);
        var filter = new NativeEtwIdentityFilter {
            ProviderName = "Microsoft-Windows-Kernel-File",
            Opcodes = new HashSet<int> { 67, 68 },
            Versions = new HashSet<int> { 3 },
            RequiredKeywords = 0x10
        };

        Assert.IsTrue(filter.Matches(envelope));
    }

    [TestMethod]
    public void NativeEtwIdentityFilter_RejectsBeforePayloadMaterializationWhenIdentityDiffers()
    {
        var envelope = CreateEnvelope(opcode: 76, version: 3, keywords: 0x10);
        var filter = new NativeEtwIdentityFilter {
            ProviderName = "Microsoft-Windows-Kernel-File",
            Opcodes = new HashSet<int> { 67, 68 }
        };

        Assert.IsFalse(filter.Matches(envelope));
    }

    [TestMethod]
    public void NativeEtwEnvelope_ToDictionary_IncludesForensicIdentityFields()
    {
        var envelope = CreateEnvelope(opcode: 67, version: 3, keywords: 0x10) with {
            Channel = 16,
            ProcessorId = 2,
            TimestampRaw = 123456789,
            RelatedActivityId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            PayloadLength = 128
        };

        var fields = envelope.ToDictionary();

        Assert.AreEqual(envelope.ProviderGuid, fields["ProviderGuid"]);
        Assert.AreEqual(67, fields["Opcode"]);
        Assert.AreEqual(3, fields["Version"]);
        Assert.AreEqual(16, fields["Channel"]);
        Assert.AreEqual(2, fields["ProcessorId"]);
        Assert.AreEqual(123456789L, fields["TimestampRaw"]);
        Assert.AreEqual(envelope.RelatedActivityId, fields["RelatedActivityId"]);
        Assert.AreEqual(128, fields["PayloadLength"]);
    }

    private static NativeEtwEnvelope CreateEnvelope(int opcode, int version, long keywords) => new()
    {
        ProviderGuid = Guid.Parse("90cbdc39-4a3e-11d1-84f4-0000f80464e3"),
        ProviderName = "Microsoft-Windows-Kernel-File",
        EventId = 0,
        Opcode = opcode,
        OpcodeName = "ReadFile",
        Version = version,
        Keywords = keywords,
        Level = 4,
        LevelName = "Informational",
        Task = 0,
        TimestampUtc = DateTimeOffset.Parse("2026-07-01T18:22:31Z").UtcDateTime,
        ProcessId = 4820,
        ThreadId = 9124,
        ActivityId = Guid.Parse("11111111-1111-1111-1111-111111111111")
    };
}
