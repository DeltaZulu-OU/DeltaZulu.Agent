using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Enrichment;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class RpcOperationResolverTests
{
    [TestMethod]
    public void Resolve_ScmrCreateServiceW_ReturnsServiceCreateHintWithoutDetectionVerdict()
    {
        var resolved = RpcOperationResolver.Resolve("{367ABB81-9844-35F1-AD32-98F038001003}", 12);

        Assert.IsNotNull(resolved);
        Assert.AreEqual("367abb81-9844-35f1-ad32-98f038001003", resolved.InterfaceUuid);
        Assert.AreEqual("MS-SCMR", resolved.InterfaceName);
        Assert.AreEqual("RCreateServiceW", resolved.OperationName);
        Assert.AreEqual("ServiceCreate", resolved.OperationCategory);
        Assert.AreEqual(RpcOperationResolver.CurrentResolverVersion, resolved.ResolverVersion);
        Assert.IsFalse(resolved.OperationCategory.Contains("RemoteServiceCreation", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(resolved.OperationCategory.Contains("LateralMovement", StringComparison.OrdinalIgnoreCase));
    }

    [DataTestMethod]
    [DataRow(0, "RCloseServiceHandle", "ServiceControl")]
    [DataRow(1, "RControlService", "ServiceControl")]
    [DataRow(2, "RDeleteService", "ServiceDelete")]
    [DataRow(11, "RChangeServiceConfigW", "ServiceConfigChange")]
    [DataRow(15, "ROpenSCManagerW", "ServiceOpenContext")]
    [DataRow(16, "ROpenServiceW", "ServiceOpenContext")]
    [DataRow(23, "RChangeServiceConfigA", "ServiceConfigChange")]
    [DataRow(27, "ROpenSCManagerA", "ServiceOpenContext")]
    [DataRow(28, "ROpenServiceA", "ServiceOpenContext")]
    [DataRow(31, "RStartServiceA", "ServiceStart")]
    [DataRow(36, "RChangeServiceConfig2A", "ServiceConfigChange")]
    [DataRow(37, "RChangeServiceConfig2W", "ServiceConfigChange")]
    [DataRow(44, "RCreateServiceWOW64A", "ServiceCreate")]
    [DataRow(45, "RCreateServiceWOW64W", "ServiceCreate")]
    [DataRow(60, "RCreateWowService", "ServiceCreate")]
    public void Resolve_ScmrKnownOperations_ReturnsExpectedOperation(int opnum, string operation, string category)
    {
        var resolved = RpcOperationResolver.Resolve("367abb81-9844-35f1-ad32-98f038001003", opnum);

        Assert.IsNotNull(resolved);
        Assert.AreEqual(operation, resolved.OperationName);
        Assert.AreEqual(category, resolved.OperationCategory);
    }

    [TestMethod]
    public void Resolve_DrsGetNcChanges_ReturnsReplicationHintWithoutDcsyncVerdict()
    {
        var resolved = RpcOperationResolver.Resolve("e3514235-4b06-11d1-ab04-00c04fc2dcd2", 3);

        Assert.IsNotNull(resolved);
        Assert.AreEqual("MS-DRSR", resolved.InterfaceName);
        Assert.AreEqual("IDL_DRSGetNCChanges", resolved.OperationName);
        Assert.AreEqual("DirectoryReplicationGetChanges", resolved.OperationCategory);
        Assert.IsFalse(resolved.OperationCategory.Contains("DCSync", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Resolve_UnknownRpcCall_ReturnsNullSoCallerCanRetainRawEvent()
    {
        var resolved = RpcOperationResolver.Resolve("00000000-0000-0000-0000-000000000000", 999);

        Assert.IsNull(resolved);
    }
}
