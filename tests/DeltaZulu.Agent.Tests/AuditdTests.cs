using DeltaZulu.Agent.Inputs.Auditd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class AuditdTests
{
    [TestMethod]
    public void Parse_DecodesExecveArgumentsAndCoercesScalarFields()
    {
        var record = new AuditdRecordParser().Parse("type=EXECVE msg=audit(1710000000.123:42): argc=2 a0=2F62696E2F7368 a1=2D63 uid=1000 exe=\"/bin/bash\" missing=(null) arch=c000003e");

        Assert.AreEqual("1710000000.123:42", record.Id);
        Assert.AreEqual("EXECVE", record.Type);
        Assert.AreEqual(2L, record.Fields["argc"]);
        Assert.AreEqual("/bin/sh", record.Fields["a0"]);
        Assert.AreEqual("-c", record.Fields["a1"]);
        Assert.AreEqual(1000L, record.Fields["uid"]);
        Assert.AreEqual("/bin/bash", record.Fields["exe"]);
        Assert.IsNull(record.Fields["missing"]);
        Assert.AreEqual("c000003e", record.Fields["arch"]);
    }

    [TestMethod]
    public void Parse_RejectsLinesWithoutAuditPrefix() => Assert.ThrowsExactly<FormatException>(() => new AuditdRecordParser().Parse("not an audit record"));

    [TestMethod]
    public void Flush_GroupsRecordsAndBuildsOrderedArgvForExecve()
    {
        var parser = new AuditdRecordParser();
        var assembler = new AuditdEventAssembler();
        const string id = "1710000000.123:42";

        Assert.IsNull(assembler.Accept(parser.Parse($"type=SYSCALL msg=audit({id}): syscall=59 comm=\"bash\"")));
        Assert.IsNull(assembler.Accept(parser.Parse($"type=EXECVE msg=audit({id}): argc=2 a1=2D6C a0=2F62696E2F62617368")));
        Assert.IsNull(assembler.Accept(parser.Parse($"type=PATH msg=audit({id}): item=0 name=\"/bin/bash\"")));

        var sourceEvent = assembler.Flush(id);

        Assert.IsNotNull(sourceEvent);
        Assert.AreEqual("LinuxAuditd", sourceEvent.Metadata.SourceType);
        Assert.AreEqual(id, sourceEvent.Fields["ID"]);
        var execve = Assert.IsInstanceOfType<Dictionary<string, object?>>(sourceEvent.Fields["EXECVE"]);
        CollectionAssert.AreEqual(new object?[] { "/bin/bash", "-l" }, Assert.IsInstanceOfType<object?[]>(execve["ARGV"]));
        var paths = Assert.IsInstanceOfType<List<Dictionary<string, object?>>>(sourceEvent.Fields["PATH"]);
        Assert.AreEqual(1, paths.Count);
        Assert.AreEqual("/bin/bash", paths[0]["name"]);
    }

    [TestMethod]
    public void FlushAll_EmitsAllPendingEventsOnce()
    {
        var parser = new AuditdRecordParser();
        var assembler = new AuditdEventAssembler();

        assembler.Accept(parser.Parse("type=SYSCALL msg=audit(1.1:1): syscall=59"));
        assembler.Accept(parser.Parse("type=SYSCALL msg=audit(1.1:2): syscall=60"));

        Assert.AreEqual(2, assembler.FlushAll().Count());
        Assert.AreEqual(0, assembler.FlushAll().Count());
    }
}
