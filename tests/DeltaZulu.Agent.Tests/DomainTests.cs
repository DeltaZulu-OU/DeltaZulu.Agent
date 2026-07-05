using DeltaZulu.Pipeline.Core.Delivery;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Observability;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class DomainTests
{
    [TestMethod]
    public void Pipeline_Assembly_Has_No_Project_Dependencies()
    {
        var pipelineAssembly = typeof(SourceEvent).Assembly;
        var referencedAssemblies = pipelineAssembly.GetReferencedAssemblies();
        var projectReferences = referencedAssemblies
            .Where(a => a.Name!.StartsWith("DeltaZulu.", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Name!)
            .ToList();

        Assert.IsEmpty(projectReferences,
            $"Pipeline assembly should have no DeltaZulu project dependencies but references: {string.Join(", ", projectReferences)}");
    }

    [TestMethod]
    public void Pipeline_Types_Are_In_Pipeline_Assembly()
    {
        const string domainAssemblyName = "DeltaZulu.Pipeline.Core";

        Assert.AreEqual(domainAssemblyName, typeof(SourceEvent).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(ResourceMetadata).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(ResourceOutputRecord).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(DictionaryCoercion).Assembly.GetName().Name);

        Assert.AreEqual(domainAssemblyName, typeof(ResourceProfile).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(ResourceDescriptor).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(ResourceFilter).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(ResourceInputContract).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(ResourceOutputContract).Assembly.GetName().Name);

        Assert.AreEqual(domainAssemblyName, typeof(DeliveryRecord).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(DeliveryBatch).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(DeliveryAck).Assembly.GetName().Name);

        Assert.AreEqual(domainAssemblyName, typeof(CollectorObservationMetadata).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(LogTelemetryKey).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(PipelineCountsObservation).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(FilterSummaryObservation).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(SourceHealthObservation).Assembly.GetName().Name);
        Assert.AreEqual(domainAssemblyName, typeof(LocalCoverageStateObservation).Assembly.GetName().Name);
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
    public void DeliveryRecord_FromResourceOutput_Creates_Valid_Record()
    {
        var metadata = new Dictionary<string, object?> {
            ["collectorId"] = "agent-1",
            ["sourceType"] = "syslog",
            ["sourceName"] = "auth.log"
        };
        var eventData = new Dictionary<string, object?> { ["Message"] = "test" };
        var output = new ResourceOutputRecord { Metadata = metadata, Event = eventData };

        var delivery = DeliveryRecord.FromResourceOutput(output);

        Assert.AreEqual("agent-1", delivery.AgentId);
        Assert.AreEqual("syslog:auth.log", delivery.SourceId);
        Assert.IsNotNull(delivery.DeliveryId);
    }
}