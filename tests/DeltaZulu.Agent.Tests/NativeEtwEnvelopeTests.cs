using DeltaZulu.Pipeline.Inputs.Etw;

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
    public void NativeEtwIdentityFilter_HonorsEventIdAllowAndDenyLists()
    {
        var filter = new NativeEtwIdentityFilter {
            EventIds = new HashSet<int> { 1, 2 },
            ExcludedEventIds = new HashSet<int> { 2 }
        };

        Assert.IsTrue(filter.Matches(CreateEnvelope(eventId: 1)));
        Assert.IsFalse(filter.Matches(CreateEnvelope(eventId: 2)));
        Assert.IsFalse(filter.Matches(CreateEnvelope(eventId: 3)));
    }

    [TestMethod]
    public void EtwSelfProcessFilter_DropsOnlyCurrentProcessEvents()
    {
        Assert.IsTrue(EtwSelfProcessFilter.IsSelfProcessEvent(EtwSelfProcessFilter.CurrentProcessId));
        Assert.IsNotEmpty(EtwSelfProcessFilter.CurrentAgentProcessNames);
        Assert.IsTrue(EtwSelfProcessFilter.IsSelfProcessEvent(0, EtwSelfProcessFilter.CurrentAgentProcessNames.First()));
        Assert.IsFalse(EtwSelfProcessFilter.IsSelfProcessEvent(0));
        Assert.IsFalse(EtwSelfProcessFilter.IsSelfProcessEvent(0, "notepad"));
    }

    [TestMethod]
    public void EtwCollectorMetrics_CountsSelfProcessDrops()
    {
        var metrics = new EtwCollectorMetrics();

        metrics.IncrementEtwCallbackSelfProcessEventsDropped();

        Assert.AreEqual(1, metrics.EtwCallbackSelfProcessEventsDropped);
    }

    [TestMethod]
    public void EtwNativeFilterCompiler_ReturnsNullWhenProfileHasNoNativeConstraints()
    {
        var profile = new DeltaZulu.Pipeline.Core.Profiles.ResourceProfile {
            Resource = new DeltaZulu.Pipeline.Core.Profiles.ResourceDescriptor { Family = "etw" }
        };

        Assert.IsNull(EtwNativeFilterCompiler.Compile(profile));
    }

    [TestMethod]
    public void EtwNativeFilterCompiler_CompilesProviderEventOpcodeAndVersionConstraints()
    {
        var providerGuid = Guid.Parse("90cbdc39-4a3e-11d1-84f4-0000f80464e3");
        var profile = new DeltaZulu.Pipeline.Core.Profiles.ResourceProfile {
            Resource = new DeltaZulu.Pipeline.Core.Profiles.ResourceDescriptor {
                Family = "etw",
                Provider = "Microsoft-Windows-Kernel-File",
                ProviderGuid = providerGuid,
                Options = new Dictionary<string, object?> {
                    ["eventIds"] = new List<object?> { 10, 11 },
                    ["excludedEventIds"] = new List<object?> { 11 },
                    ["opcodes"] = new List<object?> { 67 },
                    ["versions"] = new List<object?> { 3 }
                }
            }
        };

        var filter = EtwNativeFilterCompiler.Compile(profile);

        Assert.IsNotNull(filter);
        Assert.IsTrue(filter.Matches("Microsoft-Windows-Kernel-File", providerGuid, 10, 67, 3, 0));
        Assert.IsFalse(filter.Matches("Microsoft-Windows-Kernel-File", providerGuid, 11, 67, 3, 0));
        Assert.IsFalse(filter.Matches("Other", providerGuid, 10, 67, 3, 0));
        Assert.IsFalse(filter.Matches("Microsoft-Windows-Kernel-File", providerGuid, 10, 68, 3, 0));
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

    private static NativeEtwEnvelope CreateEnvelope(int opcode = 67, int version = 3, long keywords = 0x10, int eventId = 0) => new()
    {
        ProviderGuid = Guid.Parse("90cbdc39-4a3e-11d1-84f4-0000f80464e3"),
        ProviderName = "Microsoft-Windows-Kernel-File",
        EventId = eventId,
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
