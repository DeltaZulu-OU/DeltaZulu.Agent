using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class AcquisitionIdentityTests
{
    [TestMethod]
    public void SourceShapedProjection_PreservesAcquisitionIdentity()
    {
        var source = new SourceEvent(
            new ResourceMetadata { SourceName = "Security", EventUid = "acquisition-1" },
            new Dictionary<string, object?> { ["EventId"] = 4625 });
        var row = source.ToKqlRow();
        row["_metadata"] = new Dictionary<string, object?>();
        var output = ResourceOutputRecord.FromKqlProjection(
            new Dictionary<string, object?>(row), "profile", "1", source.Metadata, source);
        Assert.AreEqual(ResourceOutputRecord.SourceShapedQueryResult,
            output.Metadata[ResourceOutputRecord.QueryResultShapeMetadataKey]);
        Assert.AreEqual("acquisition-1", output.Metadata["eventUid"]);
    }

    [TestMethod]
    public void SourceShapedProjection_CannotReplaceAcquisitionIdentity()
    {
        var source = new SourceEvent(
            new ResourceMetadata { EventUid = "acquisition-1" },
            new Dictionary<string, object?> { ["EventId"] = 4625 });
        var row = source.ToKqlRow();
        row["_metadata"] = new Dictionary<string, object?> { ["eventUid"] = "replacement" };
        var output = ResourceOutputRecord.FromKqlProjection(
            new Dictionary<string, object?>(row), "profile", "1", source.Metadata, source);
        Assert.AreEqual("acquisition-1", output.Metadata["eventUid"]);
    }

    [TestMethod]
    public void IndependentAcquisitions_ReceiveDistinctIdentifiers()
    {
        var first = new ResourceMetadata();
        var second = new ResourceMetadata();
        Assert.AreNotEqual(first.EventUid, second.EventUid);
    }
}
