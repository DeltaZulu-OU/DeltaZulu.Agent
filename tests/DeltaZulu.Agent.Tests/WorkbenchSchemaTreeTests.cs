using DeltaZulu.Agent.ProfileWorkbench;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class WorkbenchSchemaTreeTests
{
    [TestMethod]
    public void Build_UsesVirtualRootWithPrimaryLogSourcesAndFields()
    {
        var tree = WorkbenchSchemaTree.Build([
            CreateProfile("eventlog", "Eventlog", "Security", null, "EventId:int,TargetUserName:string"),
            CreateProfile("etw", "ETW", null, "Windows-Kernel-Process", "EventId:int,Image:string")
        ]);

        Assert.AreEqual("none", tree.Text);
        Assert.AreSequenceEqual(
            new[] { "ETW (source: Windows-Kernel-Process)", "Eventlog (source: Security)" }, tree.Children.Select(child => child.Text).ToArray());
        Assert.AreSequenceEqual(
            new[] { "EventId: int", "Image: string", "source: string", "_metadata: dynamic" }, tree.Children[0].Children.Select(child => child.Text).ToArray());
        Assert.AreEqual("test.etw.Windows-Kernel-Process", tree.Children[0].ProfileId);
        Assert.IsTrue(tree.Children[0].Children.All(child => child.ProfileId == tree.Children[0].ProfileId));
        Assert.AreSequenceEqual(
            new[] { "EventId: int", "TargetUserName: string", "source: string", "_metadata: dynamic" }, tree.Children[1].Children.Select(child => child.Text).ToArray());
        Assert.AreEqual("test.eventlog.Security", tree.Children[1].ProfileId);
        Assert.IsTrue(tree.Children[1].Children.All(child => child.ProfileId == tree.Children[1].ProfileId));
    }

    [TestMethod]
    public void InsertionText_LogSource_InsertsSourcePredicate()
    {
        var etw = WorkbenchSchemaTree.Build([
            CreateProfile("etw", "ETW", null, "Windows-Kernel-Process", "EventId:int")
        ]).Children.Single();

        Assert.AreEqual("ETW | Source ~= \"Windows-Kernel-Process\" ", WorkbenchSchemaTree.InsertionText(etw));
    }

    [TestMethod]
    public void Build_TrimsOperationalSuffixFromDefaultWindowsSources()
    {
        var eventlog = WorkbenchSchemaTree.Build([
            CreateProfile("eventlog", "Eventlog", "Microsoft-Windows-AppLocker/EXE and DLL/Operational", null, "EventId:int")
        ]).Children.Single();

        Assert.AreEqual("Eventlog (source: Microsoft-Windows-AppLocker/EXE and DLL)", eventlog.Text);
        Assert.AreEqual("Eventlog | Source ~= \"Microsoft-Windows-AppLocker/EXE and DLL\" ", WorkbenchSchemaTree.InsertionText(eventlog));
    }

    [TestMethod]
    public void Build_ExpandsKnownNativeSchemaAliasesToFieldChildren()
    {
        var syslog = WorkbenchSchemaTree.Build([
            CreateProfile("syslog", "EventLog", null, null, "LinuxSyslog.Native", service: "sshd")
        ]).Children.Single();

        Assert.AreEqual("Eventlog (source: sshd)", syslog.Text);
        Assert.Contains("Message: string", syslog.Children.Select(child => child.Text).ToArray());
        Assert.Contains("ProcessName: string", syslog.Children.Select(child => child.Text).ToArray());
        Assert.Contains("source: string", syslog.Children.Select(child => child.Text).ToArray());
        Assert.Contains("_metadata: dynamic", syslog.Children.Select(child => child.Text).ToArray());
    }

    [TestMethod]
    public void Build_ExposesColumnTypesAndInsertsOnlyTheColumnName()
    {
        var field = WorkbenchSchemaTree.Build([
            CreateProfile("syslog", "Syslog", null, null, "Message:string", service: "sshd")
        ]).Children.Single().Children.Single(child => child.Field == "Message");

        Assert.AreEqual("Message: string", field.Text);
        Assert.AreEqual("string", field.KqlType);
        Assert.AreEqual("Message", WorkbenchSchemaTree.InsertionText(field));
    }

    private static ResourceProfile CreateProfile(string family, string table, string? channel, string? provider, string schema, string? service = null) => new()
    {
        Id = $"test.{family}.{channel ?? provider}",
        Name = "test",
        Enabled = true,
        Resource = new ResourceDescriptor { Platform = "test", Family = family, Channel = channel, Provider = provider, Service = service },
        Input = new ResourceInputContract { Table = table, Schema = schema },
        Output = new ResourceOutputContract { Format = "ndjson", PreserveOriginalFieldNames = true }
    };
}
