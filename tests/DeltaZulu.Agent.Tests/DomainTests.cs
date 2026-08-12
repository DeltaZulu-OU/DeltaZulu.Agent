using DeltaZulu.Pipeline.Core;
using DeltaZulu.Pipeline.Core.Delivery;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Observability;
using DeltaZulu.Pipeline.Core.Profiles;
using DeltaZulu.Pipeline.Enrichment.Events;
using DeltaZulu.Pipeline.Inputs.Auditd;
using DeltaZulu.Pipeline.Outputs.Ndjson;
using DeltaZulu.Pipeline.Tunnel;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class DomainTests
{
    [TestMethod]
    public void Pipeline_Types_Are_In_Pipeline_Assembly()
    {
        const string domainAssemblyName = "DeltaZulu.Pipeline";

        Assert.AreEqual(domainAssemblyName, typeof(SourceEvent).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(ResourceMetadata).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(ResourceOutputRecord).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(DictionaryCoercion).Assembly.GetName().Name);

        Assert.AreEqual(domainAssemblyName, typeof(ResourceProfile).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(ResourceDescriptor).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(ResourceFilter).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(ResourceInputContract).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(ResourceOutputContract).Assembly.GetName().Name);

        Assert.AreEqual(domainAssemblyName, typeof(ResourceOutputRecordForwardMapper).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(DeliveryAck).Assembly.GetName().Name);

        Assert.AreEqual(domainAssemblyName, typeof(CollectorObservationMetadata).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(LogTelemetryKey).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(PipelineCountsObservation).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(FilterSummaryObservation).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(SourceHealthObservation).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(LocalCoverageStateObservation).Assembly.GetName().Name);

        Assert.AreEqual(domainAssemblyName, typeof(ResourcePipeline).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(AuditdEventAssembler).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(ConsoleNdjsonSink).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(ResourceOutputEnricher).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(TcpTunnel).Assembly.GetName().Name);
    }

    [TestMethod]
    public void SourceEvent_ToKqlRow_Includes_Metadata()
    {
        var metadata = new ResourceMetadata { SourceType = "test", SourceName = "unit" };
        var fields = new Dictionary<string, object?> { ["key"] = "value" };
        var source = new SourceEvent(metadata, fields);

        var row = source.ToKqlRow();

        Assert.AreEqual("value", row["key"]);
        Assert.IsNotNull(row["_metadata"]);
    }

    [TestMethod]
    public void ResourceOutputRecord_FromSource_Preserves_Fields()
    {
        var metadata = new ResourceMetadata { SourceType = "test", SourceName = "unit" };
        var fields = new Dictionary<string, object?> { ["EventId"] = 42 };
        var source = new SourceEvent(metadata, fields);

        var record = ResourceOutputRecord.FromSource(source, "profile1", "1.0");

        Assert.AreEqual("profile1", record.Metadata["profileId"]);
        Assert.AreEqual(42, record.Event["EventId"]);
    }

    [TestMethod]
    public void ResourceOutputRecordForwardMapper_ToForwardLogRecord_Creates_Valid_Record()
    {
        var metadata = new Dictionary<string, object?> {
            ["collectorId"] = "agent-1",
            ["sourceType"] = "syslog",
            ["sourceName"] = "auth.log"
        };
        var eventData = new Dictionary<string, object?> { ["Message"] = "test" };
        var output = new ResourceOutputRecord { Metadata = metadata, Event = eventData };

        var delivery = ResourceOutputRecordForwardMapper.ToForwardLogRecord(output);

        Assert.AreEqual("agent-1", delivery.AgentId);
        Assert.AreEqual("syslog", delivery.SourceType);
        Assert.AreEqual("auth.log", delivery.SourceName);
        Assert.IsFalse(string.IsNullOrWhiteSpace(delivery.DeliveryId));
    }
}
