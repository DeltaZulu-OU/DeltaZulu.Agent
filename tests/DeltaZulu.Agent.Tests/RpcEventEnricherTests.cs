using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Enrichment;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class RpcEventEnricherTests
{
    [TestMethod]
    public void BuildRpcEnrichment_KnownRemoteRpcCall_EmitsSemanticRpcFields()
    {
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["InterfaceUuid"] = "{367ABB81-9844-35F1-AD32-98F038001003}",
            ["ProcNum"] = 12,
            ["Endpoint"] = "49679",
            ["NetworkAddress"] = "192.168.10.25"
        };

        var enrichment = RpcEventEnricher.BuildRpcEnrichment(fields);

        Assert.IsNotNull(enrichment);
        var rpc = (IReadOnlyDictionary<string, object?>)enrichment["Rpc"]!;
        Assert.AreEqual("367abb81-9844-35f1-ad32-98f038001003", rpc["InterfaceUuid"]);
        Assert.AreEqual("MS-SCMR", rpc["InterfaceName"]);
        Assert.AreEqual(12, rpc["ProcNum"]);
        Assert.AreEqual("RCreateServiceW", rpc["OperationName"]);
        Assert.AreEqual("ServiceCreate", rpc["OperationCategory"]);
        Assert.AreEqual("49679", rpc["Endpoint"]);
        Assert.AreEqual("192.168.10.25", rpc["NetworkAddress"]);
        Assert.AreEqual(false, rpc["IsLocal"]);
        Assert.AreEqual(true, rpc["IsRemote"]);
        Assert.AreEqual(RpcOperationResolver.CurrentResolverVersion, rpc["ResolverVersion"]);
    }


    [TestMethod]
    public void ResourceOutputEnricher_AttachesRpcEnrichmentAfterFilterWhenRpcFieldsArePresent()
    {
        var source = new SourceEvent(
            new ResourceMetadata { SourceType = "Etw", SourceName = "Microsoft-Windows-RPC", RawPreserved = true },
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["InterfaceUuid"] = "367abb81-9844-35f1-ad32-98f038001003",
                ["ProcNum"] = 12,
                ["NetworkAddress"] = "192.168.10.25"
            });

        var preEnrichmentOutput = ResourceOutputRecord.FromSource(source);
        Assert.IsNull(preEnrichmentOutput.Enrichment);

        var output = ResourceOutputEnricher.EnrichAfterFilter(preEnrichmentOutput);

        Assert.IsNotNull(output.Enrichment);
        var rpc = (IReadOnlyDictionary<string, object?>)output.Enrichment["Rpc"]!;
        Assert.AreEqual("MS-SCMR", rpc["InterfaceName"]);
        Assert.AreEqual("RCreateServiceW", rpc["OperationName"]);
        Assert.AreEqual("ServiceCreate", rpc["OperationCategory"]);
        Assert.AreEqual(true, rpc["IsRemote"]);
    }


    [TestMethod]
    public void ResourceOutputRecord_FromSource_DoesNotAttachRpcEnrichmentForNonRpcOpNumOnlyRecord()
    {
        var source = new SourceEvent(
            new ResourceMetadata { SourceType = "Application", SourceName = "Contoso-App", RawPreserved = true },
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["OpNum"] = 12,
                ["Message"] = "Non-RPC business operation"
            });

        var output = ResourceOutputEnricher.EnrichAfterFilter(ResourceOutputRecord.FromSource(source));

        Assert.IsNull(output.Enrichment);
    }

    [TestMethod]
    public void ResourceOutputRecord_FromKqlProjection_PreservesCallerProvidedEnrichment()
    {
        var providedEnrichment = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Custom"] = new Dictionary<string, object?> { ["Value"] = "keep" }
        };
        var projected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["InterfaceUuid"] = "367abb81-9844-35f1-ad32-98f038001003",
            ["ProcNum"] = 12,
            ["enrichment"] = providedEnrichment
        };
        var metadata = new ResourceMetadata { SourceType = "WindowsEtw", SourceName = "Microsoft-Windows-RPC", RawPreserved = true };

        var preEnrichmentOutput = ResourceOutputRecord.FromKqlProjection(projected, "windows.etw.rpc.p0", "1.1.0", metadata);
        var output = ResourceOutputEnricher.EnrichAfterFilter(preEnrichmentOutput);

        Assert.IsNotNull(output.Enrichment);
        Assert.IsTrue(output.Enrichment.ContainsKey("Custom"));
        Assert.IsFalse(output.Enrichment.ContainsKey("Rpc"));
    }

    [TestMethod]
    public void ResourceOutputRecord_FromKqlProjection_BuildsRpcEnrichmentForRpcProviderWhenAbsent()
    {
        var projected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProviderName"] = "Microsoft-Windows-RPC",
            ["InterfaceUuid"] = "367abb81-9844-35f1-ad32-98f038001003",
            ["ProcNum"] = 12,
            ["NetworkAddress"] = "192.168.10.25"
        };
        var metadata = new ResourceMetadata { SourceType = "WindowsEtw", SourceName = "Etw", RawPreserved = true };

        var preEnrichmentOutput = ResourceOutputRecord.FromKqlProjection(projected, "windows.etw.rpc.p0", "1.1.0", metadata);
        Assert.IsNull(preEnrichmentOutput.Enrichment);

        var output = ResourceOutputEnricher.EnrichAfterFilter(preEnrichmentOutput);

        Assert.IsNotNull(output.Enrichment);
        var rpc = (IReadOnlyDictionary<string, object?>)output.Enrichment["Rpc"]!;
        Assert.AreEqual("MS-SCMR", rpc["InterfaceName"]);
    }

    [DataTestMethod]
    [DataRow("LRPC-1234", "192.168.10.25")]
    [DataRow("49679", null)]
    [DataRow("49679", "")]
    [DataRow("49679", "NULL")]
    [DataRow("49679", "localhost")]
    [DataRow("49679", "127.0.0.1")]
    [DataRow("49679", "::1")]
    public void IsLikelyLocalRpc_LocalForms_ReturnTrue(string endpoint, string? networkAddress)
    {
        Assert.IsTrue(RpcEventEnricher.IsLikelyLocalRpc(endpoint, networkAddress));
    }

    [TestMethod]
    public void BuildRpcEnrichment_UnknownRpcCall_RetainsRawRpcIdentityWithoutSemanticNames()
    {
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["InterfaceUuid"] = "00000000-0000-0000-0000-000000000000",
            ["OpNum"] = "999",
            ["NetworkAddress"] = "10.0.0.5"
        };

        var enrichment = RpcEventEnricher.BuildRpcEnrichment(fields);

        Assert.IsNotNull(enrichment);
        var rpc = (IReadOnlyDictionary<string, object?>)enrichment["Rpc"]!;
        Assert.AreEqual("00000000-0000-0000-0000-000000000000", rpc["InterfaceUuid"]);
        Assert.AreEqual(999, rpc["ProcNum"]);
        Assert.IsFalse(rpc.ContainsKey("InterfaceName"));
        Assert.IsFalse(rpc.ContainsKey("OperationName"));
        Assert.IsFalse(rpc.ContainsKey("OperationCategory"));
    }
}
