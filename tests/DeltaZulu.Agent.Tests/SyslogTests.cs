using DeltaZulu.Agent.Inputs.Syslog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class SyslogTests
{
    [TestMethod]
    public void Parse_Rfc5424Message_DecodesPriorityStructuredFieldsAndKeyValues()
    {
        var evt = new LightweightSyslogParser().Parse("<34>1 2024-03-09T10:11:12Z host1 sshd 123 ID47 - Accepted user=bob src=10.0.0.5", "tcp", "192.0.2.10");

        Assert.AreEqual("LinuxSyslog", evt.Metadata.SourceType);
        Assert.AreEqual("host1", evt.Metadata.Hostname);
        Assert.AreEqual(34, evt.Fields["Priority"]);
        Assert.AreEqual("auth", evt.Fields["Facility"]);
        Assert.AreEqual("crit", evt.Fields["Severity"]);
        Assert.AreEqual(123, evt.Fields["ProcessId"]);
        Assert.AreEqual("Accepted user=bob src=10.0.0.5", evt.Fields["Message"]);
        Assert.AreEqual("192.0.2.10", evt.Fields["SourceIpAddress"]);
        var extracted = Assert.IsInstanceOfType<Dictionary<string, object?>>(evt.Fields["ExtractedData"]);
        Assert.AreEqual("bob", extracted["user"]);
        Assert.AreEqual("10.0.0.5", extracted["src"]);
    }

    [TestMethod]
    public void Parse_Rfc3164Message_ExtractsProcessAndQuotedKeyValues()
    {
        var evt = new LightweightSyslogParser().Parse("<38>Mar  9 10:11:12 web sudo[321]: action=\"session opened\" user=root", "file");

        Assert.AreEqual("web", evt.Fields["Hostname"]);
        Assert.AreEqual("sudo", evt.Fields["ProcessName"]);
        Assert.AreEqual(321, evt.Fields["ProcessId"]);
        var extracted = Assert.IsInstanceOfType<Dictionary<string, object?>>(evt.Fields["ExtractedData"]);
        Assert.AreEqual("session opened", extracted["action"]);
        Assert.AreEqual("root", extracted["user"]);
    }

    [TestMethod]
    public void Parse_UnstructuredMessage_PreservesRawMessageAndBody()
    {
        var evt = new LightweightSyslogParser().Parse("plain message without syslog header", "stdin");

        Assert.AreEqual("plain message without syslog header", evt.Fields["RawMessage"]);
        Assert.AreEqual("plain message without syslog header", evt.Fields["Message"]);
    }
}
