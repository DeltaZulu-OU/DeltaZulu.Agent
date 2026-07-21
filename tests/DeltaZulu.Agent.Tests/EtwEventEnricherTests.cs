using System.Diagnostics;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Enrichment.Events;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class EtwEventEnricherTests
{
    [TestMethod]
    public void EnrichAfterFilter_DotNetExceptionEvent_AttachesDotNetTimingAndQualityWithoutPidOnlyContext()
    {
        var source = new SourceEvent(
            new ResourceMetadata
            {
                SourceType = "WindowsEtw",
                SourceName = "etwDeltaZulu-DotNETRuntime",
                Hostname = "LAP29",
                RawPreserved = true
            },
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProviderGuid"] = "e13c0d23-ccbc-4e12-931b-d9cc2eee27e4",
                ["ProviderName"] = "Microsoft-Windows-DotNETRuntime",
                ["EventId"] = 80,
                ["EventName"] = "Exception/Start",
                ["Opcode"] = 1,
                ["OpcodeName"] = "Start",
                ["Task"] = 7,
                ["TaskName"] = "Exception",
                ["LevelCode"] = 2,
                ["Level"] = "Error",
                ["Keywords"] = 8589967360L,
                ["Version"] = 1,
                ["TimeStamp"] = "2026-07-05T18:02:03.8505925+03:00",
                ["ProcessId"] = int.MaxValue,
                ["ThreadId"] = 22104,
                ["ActivityId"] = "00000000-0000-0000-0000-000000000000",
                ["RelatedActivityId"] = "00000000-0000-0000-0000-000000000000",
                ["ProcessorId"] = 9,
                ["PayloadLength"] = 180,
                ["ExceptionType"] = "System.InvalidCastException",
                ["ExceptionMessage"] = "Invalid cast from 'System.UInt64' to 'System.IntPtr'.",
                ["ExceptionHRESULT"] = -2147467262,
                ["ExceptionFlags"] = 16,
                ["ClrInstanceID"] = 10
            });
        var preEnrichmentRecord = ResourceOutputRecord.FromSource(source);
        var metadata = new Dictionary<string, object?>(preEnrichmentRecord.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["ingestedAt"] = "2026-07-05T15:08:04.9250042+00:00",
            ["forwarder"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["createdAt"] = "2026-07-05T15:02:25.674311+00:00"
            }
        };
        var record = preEnrichmentRecord with { Metadata = metadata };

        var enriched = ResourceOutputEnricher.EnrichAfterFilter(record);

        Assert.IsNotNull(enriched.Enrichment);
        var etw = RequireMap(enriched.Enrichment, "Etw");
        Assert.AreEqual("Microsoft-Windows-DotNETRuntime", etw["ProviderName"]);
        Assert.AreEqual(".NET Runtime", etw["ProviderCategory"]);
        Assert.AreEqual("Exception", etw["EventCategory"]);
        Assert.IsFalse(enriched.Enrichment.ContainsKey("Process"));
        Assert.IsFalse(enriched.Enrichment.ContainsKey("Thread"));

        var dotNet = RequireMap(enriched.Enrichment, "DotNet");
        Assert.AreEqual("System.InvalidCastException", dotNet["ExceptionType"]);
        Assert.AreEqual("System", dotNet["ExceptionNamespace"]);
        Assert.AreEqual("InvalidCastException", dotNet["ExceptionClass"]);
        Assert.AreEqual("TypeConversion", dotNet["ExceptionCategory"]);
        Assert.AreEqual("0x80004002", dotNet["ExceptionHRESULTHex"]);
        Assert.AreEqual("E_NOINTERFACE", dotNet["ExceptionHRESULTSymbol"]);
        Assert.AreEqual(10, dotNet["ClrInstanceID"]);
        Assert.IsTrue(dotNet.ContainsKey("ExceptionMessageFingerprint"));
        Assert.IsTrue(dotNet.ContainsKey("ExceptionGroupKey"));

        var timing = RequireMap(enriched.Enrichment, "Timing");
        Assert.AreEqual("2026-07-05T15:02:03.8505925Z", timing["EventTimeUtc"]);
        Assert.AreEqual(361074L, timing["IngestDelayMs"]);
        Assert.AreEqual(21823L, timing["ForwarderCreateDelayMs"]);
        Assert.AreEqual(339250L, timing["ForwarderToIngestDelayMs"]);
        Assert.AreEqual("Delayed", timing["TimestampSkewStatus"]);

        var quality = RequireMap(enriched.Enrichment, "Quality");
        Assert.IsFalse((bool?)quality["HasActivityId"]);
        Assert.IsFalse((bool?)quality["HasRelatedActivityId"]);
        Assert.AreEqual(180, quality["PayloadLength"]);
    }

    [TestMethod]
    public void EnrichAfterFilter_KernelNetworkEventWithEmptyPayload_DoesNotInventProcessTimingOrNetworkContext()
    {
        var source = new SourceEvent(
            new ResourceMetadata
            {
                SourceType = "WindowsEtw",
                SourceName = "etwDeltaZulu-Kernel-Network",
                Hostname = "LAP29",
                RawPreserved = true,
                Properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["forwarder"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["createdAt"] = "2026-07-04T08:43:29.9899003+00:00"
                    }
                }
            },
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["TimeCreated"] = "",
                ["ProviderName"] = "Microsoft-Windows-Kernel-Network",
                ["EventId"] = 11,
                ["EventName"] = "KERNEL_NETWORK_TASK_TCPIP/Datareceived.",
                ["ProcessId"] = int.MaxValue,
                ["LocalIp"] = "",
                ["RemoteIp"] = "",
                ["LocalAddress"] = "",
                ["RemoteAddress"] = "",
                ["LocalPort"] = "",
                ["RemotePort"] = "",
                ["Protocol"] = "",
                ["Image"] = "",
                ["ParentImage"] = ""
            });

        var enriched = ResourceOutputEnricher.EnrichAfterFilter(ResourceOutputRecord.FromSource(source));

        Assert.IsNotNull(enriched.Enrichment);
        var etw = RequireMap(enriched.Enrichment, "Etw");
        Assert.AreEqual("Microsoft-Windows-Kernel-Network", etw["ProviderName"]);
        Assert.AreEqual("Kernel Network", etw["ProviderCategory"]);
        Assert.AreEqual("Network", etw["EventDomain"]);
        Assert.AreEqual("KERNEL_NETWORK_TASK_TCPIP", etw["EventCategory"]);
        Assert.IsFalse(enriched.Enrichment.ContainsKey("Process"));
        Assert.IsFalse(enriched.Enrichment.ContainsKey("Timing"));
        Assert.IsFalse(enriched.Enrichment.ContainsKey("Network"));
        Assert.IsFalse(enriched.Enrichment.ContainsKey("Quality"));
    }

    [TestMethod]
    public void EnrichAfterFilter_EtwEventWithLiveProcessId_AttachesProcessSnapshot()
    {
        using var currentProcess = Process.GetCurrentProcess();
        var source = new SourceEvent(
            new ResourceMetadata { SourceType = "WindowsEtw", SourceName = "etwDeltaZulu-DotNETRuntime", RawPreserved = true },
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProviderName"] = "Microsoft-Windows-DotNETRuntime",
                ["EventId"] = 80,
                ["EventName"] = "Exception/Start",
                ["ProcessId"] = currentProcess.Id,
                ["ExceptionType"] = "System.InvalidCastException"
            });

        var enriched = ResourceOutputEnricher.EnrichAfterFilter(ResourceOutputRecord.FromSource(source));

        Assert.IsNotNull(enriched.Enrichment);
        var process = RequireMap(enriched.Enrichment, "Process");
        Assert.AreEqual(currentProcess.Id, process["ProcessId"]);
        Assert.AreEqual("ObservedContext", process["ProcessIdentityStatus"]);
        Assert.AreEqual("LocalProcessSnapshot", process["ProcessResolutionSource"]);
        Assert.IsTrue(process.ContainsKey("ProcessName"));
        Assert.IsTrue(process.ContainsKey("ProcessGenerationKey"));
    }

    [TestMethod]
    public void EnrichAfterFilter_NonEtwRecord_DoesNotAttachEtwEnrichment()
    {
        var source = new SourceEvent(
            new ResourceMetadata { SourceType = "Application", SourceName = "Contoso", RawPreserved = true },
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProcessId"] = 1234,
                ["Message"] = "application payload"
            });

        var enriched = ResourceOutputEnricher.EnrichAfterFilter(ResourceOutputRecord.FromSource(source));

        Assert.IsNull(enriched.Enrichment);
    }

    private static IReadOnlyDictionary<string, object?> RequireMap(IReadOnlyDictionary<string, object?> parent, string key)
    {
        Assert.IsTrue(parent.TryGetValue(key, out var value), $"Missing enrichment key {key}.");
        return (IReadOnlyDictionary<string, object?>)value!;
    }
}
