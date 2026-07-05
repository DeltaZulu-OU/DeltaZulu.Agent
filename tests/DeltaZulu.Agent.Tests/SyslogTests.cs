using DeltaZulu.Pipeline.Inputs.Syslog;
using System.Reactive.Linq;

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
        var evt = new LightweightSyslogParser().Parse("<38>Mar  9 10:11:12 web sudo[321]: action=\"session opened\" path=\"/tmp/a\\\\b\" user=root", "file");

        Assert.AreEqual("web", evt.Fields["Hostname"]);
        Assert.AreEqual("sudo", evt.Fields["ProcessName"]);
        Assert.AreEqual(321, evt.Fields["ProcessId"]);
        var extracted = Assert.IsInstanceOfType<Dictionary<string, object?>>(evt.Fields["ExtractedData"]);
        Assert.AreEqual("session opened", extracted["action"]);
        Assert.AreEqual(@"/tmp/a\b", extracted["path"]);
        Assert.AreEqual("root", extracted["user"]);
    }

    [TestMethod]
    public void Parse_Rfc5424Message_PreservesStructuredDataWithQuotedBrackets()
    {
        const string raw = "<165>1 2024-03-09T10:11:12.123456Z router app 999 ID47 [exampleSDID@32473 note=\"contains ] bracket\" path=\"/tmp/a b\"] Configuration reload";

        var evt = new LightweightSyslogParser().Parse(raw, "tcp");

        Assert.AreEqual("2024-03-09T10:11:12.1234560+00:00", ((DateTimeOffset)evt.Fields["Timestamp"]!).ToString("O"));
        Assert.AreEqual("[exampleSDID@32473 note=\"contains ] bracket\" path=\"/tmp/a b\"]", evt.Fields["StructuredData"]);
        Assert.AreEqual("Configuration reload", evt.Fields["Message"]);
    }

    [TestMethod]
    public async Task SyslogFileTailInput_ContinuesAfterFileTruncation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deltazulu-syslog-tail-{Guid.NewGuid():N}.log");
        await File.WriteAllTextAsync(path, "<38>Mar  9 10:11:12 web sudo[321]: old=true\n");
        var input = new SyslogFileTailInput(path, "tail-test", TimeSpan.FromMilliseconds(20));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var seen = new List<string>();
        using var subscription = input.Open(cts.Token).Subscribe(evt => {
            if (evt.Fields.TryGetValue("Message", out var message) && message is string text)
            {
                lock (seen)
                {
                    seen.Add(text);
                }
            }
        });

        await File.AppendAllTextAsync(path, "<38>Mar  9 10:11:13 web sudo[321]: first=true\n");
        await WaitUntilAsync(() => {
            lock (seen)
            {
                return seen.Contains("first=true");
            }
        }, cts.Token);

        await File.WriteAllTextAsync(path, "<38>Mar  9 10:11:14 web sudo[321]: after_truncate=true\n");
        await WaitUntilAsync(() => {
            lock (seen)
            {
                return seen.Contains("after_truncate=true");
            }
        }, cts.Token);

        File.Delete(path);
    }

    [TestMethod]
    public void Parse_UnstructuredMessage_PreservesRawMessageAndBody()
    {
        var evt = new LightweightSyslogParser().Parse("plain message without syslog header", "stdin");

        Assert.AreEqual("plain message without syslog header", evt.Fields["RawMessage"]);
        Assert.AreEqual("plain message without syslog header", evt.Fields["Message"]);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(20, cancellationToken);
        }
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public void EnsureFifo_CreatesLinuxNamedPipe()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"deltazulu-fifo-{Guid.NewGuid():N}");
        var fifoPath = Path.Combine(directory, "logs.fifo");
        try
        {
            FifoSyslogInput.EnsureFifo(fifoPath);
            FifoSyslogInput.EnsureFifo(fifoPath);
        }
        finally
        {
            File.Delete(fifoPath);

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }
}
