using System.Text.Json;
using DeltaZulu.Agent.Core.Events;
using DeltaZulu.Agent.Inputs.Auditd;
using DeltaZulu.Agent.Inputs.Syslog;
using DeltaZulu.Agent.Outputs.Ndjson;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class GoldenFixtureTests
{
    private static readonly JsonSerializerOptions JsonOptions = NdjsonSerializerOptions.CreateDefault();

    // --- Syslog RFC 5424 ---

    [TestMethod]
    public void Auditd_EoeRecord_FlushesEventImmediately()
    {
        var parser = new AuditdRecordParser();
        var assembler = new AuditdEventAssembler();
        const string id = "1710000000.300:503";

        Assert.IsNull(assembler.Accept(parser.Parse($"type=SYSCALL msg=audit({id}): syscall=59 comm=\"cat\"")));
        var result = assembler.Accept(parser.Parse($"type=EOE msg=audit({id}):"));

        Assert.IsNotNull(result);
        Assert.AreEqual(id, result.Fields["ID"]);
        Assert.AreEqual(0, assembler.PendingCount);
    }

    [TestMethod]
    public void Auditd_HexEncodedPathName_DecodesInOutput()
    {
        var parser = new AuditdRecordParser();
        var assembler = new AuditdEventAssembler();
        const string id = "1710000000.500:505";

        assembler.Accept(parser.Parse($"type=SYSCALL msg=audit({id}): syscall=2 comm=\"cat\""));
        assembler.Accept(parser.Parse($"type=PATH msg=audit({id}): item=0 name=2F746D702F74657374 nametype=NORMAL"));

        var source = assembler.Flush(id)!;
        var output = ResourceOutputRecord.FromSource(source);
        var json = SerializeAndDeserialize(output);

        var paths = json.GetProperty("event").GetProperty("PATH");
        Assert.AreEqual("/tmp/test", paths[0].GetProperty("name").GetString());
    }

    [TestMethod]
    public void Auditd_MalformedLine_SkippedByParser()
    {
        var parser = new AuditdRecordParser();
        Assert.ThrowsExactly<FormatException>(() => parser.Parse("this is not an audit record"));
        Assert.ThrowsExactly<FormatException>(() => parser.Parse(""));
        Assert.ThrowsExactly<FormatException>(() => parser.Parse("type=SYSCALL without msg=audit prefix"));
    }

    [TestMethod]
    public void Auditd_MultiRecordExecve_AssemblesAndProducesCorrectArgv()
    {
        var parser = new AuditdRecordParser();
        var assembler = new AuditdEventAssembler();
        const string id = "1710000000.200:502";

        assembler.Accept(parser.Parse($"type=SYSCALL msg=audit({id}): arch=c000003e syscall=59 success=yes pid=2000 uid=1000 comm=\"bash\" exe=\"/bin/bash\""));
        assembler.Accept(parser.Parse($"type=EXECVE msg=audit({id}): argc=3 a0=2F7573722F62696E2F637572 a1=2D73 a2=68747470733A2F2F6578616D706C652E636F6D"));
        assembler.Accept(parser.Parse($"type=PATH msg=audit({id}): item=0 name=\"/usr/bin/cur\" nametype=NORMAL"));

        // a0 = hex for "/usr/bin/cur", a1 = hex for "-s", a2 = hex for "https://example.com"
        // (shortened for fixture; real paths would be longer)

        var source = assembler.Flush(id)!;
        var output = ResourceOutputRecord.FromSource(source);
        var json = SerializeAndDeserialize(output);

        var evt = json.GetProperty("event");
        Assert.AreEqual(id, evt.GetProperty("ID").GetString());
        Assert.AreEqual(3, evt.GetProperty("RawEvent").GetArrayLength());

        var execve = evt.GetProperty("EXECVE");
        var argv = execve.GetProperty("ARGV");
        Assert.AreEqual(3, argv.GetArrayLength());
        Assert.AreEqual("/usr/bin/cur", argv[0].GetString());
        Assert.AreEqual("-s", argv[1].GetString());
        Assert.AreEqual("https://example.com", argv[2].GetString());

        var paths = evt.GetProperty("PATH");
        Assert.AreEqual(1, paths.GetArrayLength());
        Assert.AreEqual("/usr/bin/cur", paths[0].GetProperty("name").GetString());
    }

    [TestMethod]
    public void Auditd_ProctitleRecord_FlushesEventAndDecodesArgv()
    {
        var parser = new AuditdRecordParser();
        var assembler = new AuditdEventAssembler();
        const string id = "1710000000.400:504";

        assembler.Accept(parser.Parse($"type=SYSCALL msg=audit({id}): syscall=59 comm=\"ls\""));
        var result = assembler.Accept(parser.Parse($"type=PROCTITLE msg=audit({id}): proctitle=6C73002D6C61"));

        Assert.IsNotNull(result);
        var output = ResourceOutputRecord.FromSource(result);
        var json = SerializeAndDeserialize(output);

        var proctitle = json.GetProperty("event").GetProperty("PROCTITLE");
        var argv = proctitle.GetProperty("ARGV");
        Assert.AreEqual(2, argv.GetArrayLength());
        Assert.AreEqual("ls", argv[0].GetString());
        Assert.AreEqual("-la", argv[1].GetString());
    }

    [TestMethod]
    public void Auditd_SingleSyscall_ProducesExpectedNdjsonEnvelope()
    {
        var parser = new AuditdRecordParser();
        var assembler = new AuditdEventAssembler();
        const string line = "type=SYSCALL msg=audit(1710000000.100:501): arch=c000003e syscall=59 success=yes exit=0 pid=1234 uid=0 comm=\"ls\" exe=\"/bin/ls\"";

        assembler.Accept(parser.Parse(line));
        var source = assembler.Flush("1710000000.100:501")!;
        var output = ResourceOutputRecord.FromSource(source);
        var json = SerializeAndDeserialize(output);

        Assert.AreEqual("LinuxAuditd", json.GetProperty("_metadata").GetProperty("sourceType").GetString());
        Assert.AreEqual("linux", json.GetProperty("_metadata").GetProperty("platform").GetString());
        Assert.IsTrue(json.GetProperty("_metadata").GetProperty("rawPreserved").GetBoolean());

        var evt = json.GetProperty("event");
        Assert.AreEqual("1710000000.100:501", evt.GetProperty("ID").GetString());
        Assert.AreEqual(1, evt.GetProperty("RawEvent").GetArrayLength());
    }

    [TestMethod]
    public void Csv_NumericAndBooleanCoercion_PreservesTypesInNdjson()
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), $"deltazulu-fixture-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(tmpPath, "Count,Rate,Active,Label\n42,3.14,true,test\n0,0.0,false,other\n");

            var input = new Inputs.Files.CsvFileInput(tmpPath, "test-csv");
            var records = new List<ResourceOutputRecord>();
            using var completed = new ManualResetEventSlim(false);

            using var subscription = input.Open(TestContext.CancellationToken).Subscribe(
                source => records.Add(ResourceOutputRecord.FromSource(source)),
                _ => completed.Set(),
                () => completed.Set());

            Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(10), TestContext.CancellationToken));

            var json0 = SerializeAndDeserialize(records[0]);
            Assert.AreEqual(42, json0.GetProperty("event").GetProperty("Count").GetInt64());
            Assert.AreEqual(3.14, json0.GetProperty("event").GetProperty("Rate").GetDouble(), 0.001);
            Assert.IsTrue(json0.GetProperty("event").GetProperty("Active").GetBoolean());
            Assert.AreEqual("test", json0.GetProperty("event").GetProperty("Label").GetString());
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                File.Delete(tmpPath);
            }
        }
    }

    [TestMethod]
    public void Csv_SimpleRows_ProduceExpectedNdjsonEnvelopes()
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), $"deltazulu-fixture-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(tmpPath, "EventId,Source,Message\n4688,Security,\"Process created: cmd.exe\"\n4624,Security,\"Logon success\"\n");

            var input = new Inputs.Files.CsvFileInput(tmpPath, "test-csv");
            var records = new List<ResourceOutputRecord>();
            using var completed = new ManualResetEventSlim(false);

            using var subscription = input.Open(TestContext.CancellationToken).Subscribe(
                source => records.Add(ResourceOutputRecord.FromSource(source)),
                _ => completed.Set(),
                () => completed.Set());

            Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(10), TestContext.CancellationToken));
            Assert.HasCount(2, records);

            var json0 = SerializeAndDeserialize(records[0]);
            Assert.AreEqual("Csv", json0.GetProperty("_metadata").GetProperty("sourceType").GetString());
            Assert.AreEqual("portable", json0.GetProperty("_metadata").GetProperty("platform").GetString());
            Assert.AreEqual(4688, json0.GetProperty("event").GetProperty("EventId").GetInt64());
            Assert.AreEqual("Security", json0.GetProperty("event").GetProperty("Source").GetString());
            Assert.AreEqual("Process created: cmd.exe", json0.GetProperty("event").GetProperty("Message").GetString());

            var json1 = SerializeAndDeserialize(records[1]);
            Assert.AreEqual(4624, json1.GetProperty("event").GetProperty("EventId").GetInt64());
            Assert.AreEqual("Logon success", json1.GetProperty("event").GetProperty("Message").GetString());
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                File.Delete(tmpPath);
            }
        }
    }

    [TestMethod]
    public void FromKqlProjection_WithoutMetadataField_PreservesDeliveryIdentityFromSource()
    {
        var sourceMetadata = new ResourceMetadata {
            CollectorId = "agent-01",
            SourceType = "LinuxSyslog",
            SourceName = "auth.log",
            Platform = "linux",
            Hostname = "host01"
        };

        var projectedFields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
            ["Message"] = "test event",
            ["EventId"] = 42
        };

        var output = ResourceOutputRecord.FromKqlProjection(projectedFields, "linux.sshd", "1.0.0", sourceMetadata);
        var json = SerializeAndDeserialize(output);

        var meta = json.GetProperty("_metadata");
        Assert.AreEqual("agent-01", meta.GetProperty("collectorId").GetString());
        Assert.AreEqual("LinuxSyslog", meta.GetProperty("sourceType").GetString());
        Assert.AreEqual("auth.log", meta.GetProperty("sourceName").GetString());
        Assert.AreEqual("linux", meta.GetProperty("platform").GetString());
        Assert.AreEqual("host01", meta.GetProperty("hostname").GetString());
        Assert.AreEqual("linux.sshd", meta.GetProperty("profileId").GetString());
    }

    // --- Metadata preservation ---
    [TestMethod]
    public void FromKqlProjection_WithPartialMetadataField_FillsMissingDeliveryIdentity()
    {
        var sourceMetadata = new ResourceMetadata {
            CollectorId = "agent-01",
            SourceType = "LinuxSyslog",
            SourceName = "auth.log",
            Platform = "linux",
            Hostname = "host01"
        };

        var projectedFields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
            ["_metadata"] = new Dictionary<string, object?> {
                ["collectorId"] = "agent-01",
                ["profileId"] = "should-be-overridden"
            },
            ["Message"] = "test"
        };

        var output = ResourceOutputRecord.FromKqlProjection(projectedFields, "linux.sshd", "1.0.0", sourceMetadata);
        var json = SerializeAndDeserialize(output);

        var meta = json.GetProperty("_metadata");
        Assert.AreEqual("agent-01", meta.GetProperty("collectorId").GetString());
        Assert.AreEqual("LinuxSyslog", meta.GetProperty("sourceType").GetString());
        Assert.AreEqual("auth.log", meta.GetProperty("sourceName").GetString());
        Assert.AreEqual("linux", meta.GetProperty("platform").GetString());
        Assert.AreEqual("host01", meta.GetProperty("hostname").GetString());
        Assert.AreEqual("linux.sshd", meta.GetProperty("profileId").GetString());
    }

    [TestMethod]
    public void Ndjson_OutputEnvelope_HasMetadataEventStructure()
    {
        var metadata = new ResourceMetadata {
            SourceType = "LinuxSyslog",
            SourceName = "auth.log",
            Platform = "linux",
            Hostname = "host01",
            ProfileId = "linux.sshd",
            ProfileVersion = "1.0.0",
            RawPreserved = true
        };
        var source = new SourceEvent(metadata, new Dictionary<string, object?> {
            ["Message"] = "Accepted publickey for root",
            ["ProcessName"] = "sshd",
            ["ProcessId"] = 1234
        });

        var output = ResourceOutputRecord.FromSource(source, "linux.sshd", "1.0.0");
        var json = SerializeAndDeserialize(output);

        Assert.IsTrue(json.TryGetProperty("_metadata", out _));
        Assert.IsTrue(json.TryGetProperty("event", out _));
        Assert.AreEqual("linux.sshd", json.GetProperty("_metadata").GetProperty("profileId").GetString());
        Assert.AreEqual(1, json.GetProperty("_metadata").GetProperty("schemaVersion").GetInt32());
    }

    [TestMethod]
    public void NdjsonFileSink_WritesOneLinePerRecord()
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), $"deltazulu-fixture-{Guid.NewGuid():N}.ndjson");
        try
        {
            using (var sink = new NdjsonFileSink(tmpPath))
            {
                sink.OnNext(new ResourceOutputRecord {
                    Metadata = new Dictionary<string, object?> { ["sourceType"] = "test" },
                    Event = new Dictionary<string, object?> { ["Message"] = "line1" }
                });
                sink.OnNext(new ResourceOutputRecord {
                    Metadata = new Dictionary<string, object?> { ["sourceType"] = "test" },
                    Event = new Dictionary<string, object?> { ["Message"] = "line2" }
                });
            }

            var lines = File.ReadAllLines(tmpPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            Assert.HasCount(2, lines);

            foreach (var line in lines)
            {
                Assert.IsFalse(line.Contains('\n'));
                var doc = JsonDocument.Parse(line);
                Assert.IsTrue(doc.RootElement.TryGetProperty("_metadata", out _));
                Assert.IsTrue(doc.RootElement.TryGetProperty("event", out _));
            }
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                File.Delete(tmpPath);
            }
        }
    }

    [TestMethod]
    public void Syslog_Rfc3164_SudoSession_ProducesExpectedNdjsonEnvelope()
    {
        const string raw = "<86>Jun 25 14:30:01 apphost sudo[12345]: pam_unix(sudo:session): session opened for user root(uid=0) by admin(uid=1000)";
        var parser = new LightweightSyslogParser();

        var source = parser.Parse(raw, "auth.log");
        var output = ResourceOutputRecord.FromSource(source);
        var json = SerializeAndDeserialize(output);

        Assert.AreEqual("LinuxSyslog", json.GetProperty("_metadata").GetProperty("sourceType").GetString());
        Assert.AreEqual("apphost", json.GetProperty("_metadata").GetProperty("hostname").GetString());

        var evt = json.GetProperty("event");
        Assert.AreEqual(86, evt.GetProperty("Priority").GetInt32());
        Assert.AreEqual("authpriv", evt.GetProperty("Facility").GetString());
        Assert.AreEqual("info", evt.GetProperty("Severity").GetString());
        Assert.AreEqual("sudo", evt.GetProperty("ProcessName").GetString());
        Assert.AreEqual(12345, evt.GetProperty("ProcessId").GetInt32());
        Assert.Contains("session opened for user root", evt!.GetProperty("Message").GetString()!);
    }

    // --- Syslog RFC 3164 ---
    [TestMethod]
    public void Syslog_Rfc3164_WithKeyValues_ExtractsDataFromMessage()
    {
        const string raw = "<38>Jun 25 08:00:00 mailhost postfix[555]: connect from client=mx.example.com port=25 status=sent";
        var parser = new LightweightSyslogParser();

        var source = parser.Parse(raw, "mail.log");
        var output = ResourceOutputRecord.FromSource(source);
        var json = SerializeAndDeserialize(output);

        var evt = json.GetProperty("event");
        var extracted = evt.GetProperty("ExtractedData");
        Assert.AreEqual("mx.example.com", extracted.GetProperty("client").GetString());
        Assert.AreEqual("25", extracted.GetProperty("port").GetString());
        Assert.AreEqual("sent", extracted.GetProperty("status").GetString());
    }

    [TestMethod]
    public void Syslog_Rfc5424_SshdAccepted_ProducesExpectedNdjsonEnvelope()
    {
        const string raw = "<38>1 2024-03-09T10:11:12Z webhost sshd 4321 - - Accepted publickey for admin from 10.0.0.5 port 22 ssh2: RSA SHA256:abc123";
        var parser = new LightweightSyslogParser();

        var source = parser.Parse(raw, "auth.log");
        var output = ResourceOutputRecord.FromSource(source);
        var json = SerializeAndDeserialize(output);

        Assert.AreEqual("LinuxSyslog", json.GetProperty("_metadata").GetProperty("sourceType").GetString());
        Assert.AreEqual("auth.log", json.GetProperty("_metadata").GetProperty("sourceName").GetString());
        Assert.AreEqual("linux", json.GetProperty("_metadata").GetProperty("platform").GetString());
        Assert.AreEqual("webhost", json.GetProperty("_metadata").GetProperty("hostname").GetString());
        Assert.IsTrue(json.GetProperty("_metadata").GetProperty("rawPreserved").GetBoolean());

        var evt = json.GetProperty("event");
        Assert.AreEqual(raw, evt.GetProperty("RawMessage").GetString());
        Assert.AreEqual(38, evt.GetProperty("Priority").GetInt32());
        Assert.AreEqual("auth", evt.GetProperty("Facility").GetString());
        Assert.AreEqual("info", evt.GetProperty("Severity").GetString());
        Assert.AreEqual("sshd", evt.GetProperty("ProcessName").GetString());
        Assert.AreEqual(4321, evt.GetProperty("ProcessId").GetInt32());
        Assert.Contains("Accepted publickey for admin", evt!.GetProperty("Message").GetString()!);
    }

    [TestMethod]
    public void Syslog_Rfc5424_WithStructuredData_PreservesStructuredDataField()
    {
        const string raw = "<165>1 2024-03-09T10:11:12Z router syslog-ng 999 ID47 [exampleSDID@32473 eventSource=\"Application\"] Configuration reload";
        var parser = new LightweightSyslogParser();

        var source = parser.Parse(raw, "syslog-tcp");
        var output = ResourceOutputRecord.FromSource(source);
        var json = SerializeAndDeserialize(output);

        var evt = json.GetProperty("event");
        Assert.AreEqual(165, evt.GetProperty("Priority").GetInt32());
        Assert.AreEqual("local4", evt.GetProperty("Facility").GetString());
        Assert.AreEqual("notice", evt.GetProperty("Severity").GetString());
        Assert.AreEqual("router", evt.GetProperty("Hostname").GetString());
        Assert.AreEqual("syslog-ng", evt.GetProperty("AppName").GetString());
        Assert.Contains("exampleSDID@32473", evt.GetProperty("StructuredData").GetString()!);
    }

    // --- Syslog unstructured ---

    [TestMethod]
    public void Syslog_Unstructured_PreservesRawMessageAsEvent()
    {
        const string raw = "kernel: [12345.678] TCP: out of memory -- consider tuning tcp_mem";
        var parser = new LightweightSyslogParser();

        var source = parser.Parse(raw, "kern.log");
        var output = ResourceOutputRecord.FromSource(source);
        var json = SerializeAndDeserialize(output);

        var evt = json.GetProperty("event");
        Assert.AreEqual(raw, evt.GetProperty("RawMessage").GetString());
        Assert.AreEqual(raw, evt.GetProperty("Message").GetString());
    }

    // --- Auditd single record ---
    // --- Auditd multi-record EXECVE event ---
    // --- Auditd EOE completion ---
    // --- Auditd PROCTITLE completion ---
    // --- Auditd malformed line handling ---
    // --- Auditd hex PATH name decoding ---
    // --- CSV fixture ---
    // --- NDJSON roundtrip envelope ---
    // --- NDJSON file sink roundtrip ---
    private static JsonElement SerializeAndDeserialize(ResourceOutputRecord record)
    {
        var json = JsonSerializer.Serialize(record, JsonOptions);
        Assert.IsFalse(json.Contains(Environment.NewLine, StringComparison.Ordinal), "NDJSON must be single-line");
        return JsonDocument.Parse(json).RootElement;
    }

    public TestContext TestContext { get; set; }
}