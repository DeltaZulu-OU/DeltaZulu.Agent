using System.Reactive.Linq;
using System.Reactive.Subjects;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;
using DeltaZulu.Agent.Filter.Kql;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class KqlTests
{
    [TestMethod]
    public void Execute_PropagatesSourceErrorsToOutputObserver()
    {
        using var executor = new ResourceKqlProfileExecutor();
        var sourceException = new UnauthorizedAccessException("denied");
        Exception? observedException = null;
        using var completed = new ManualResetEventSlim(false);

        using var subscription = executor
            .Execute(Observable.Throw<SourceEvent>(sourceException), CreatePassThroughProfile(), TestContext.CancellationToken)
            .Subscribe(
                _ => { },
                error => {
                    observedException = error;
                    completed.Set();
                },
                completed.Set);

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5), TestContext.CancellationToken), "The output observer did not receive source termination.");
        Assert.AreSame(sourceException, observedException);
    }

    [TestMethod]
    public void Execute_SourceErrorDoesNotThrowWhenObserverDisposesSubscription()
    {
        using var executor = new ResourceKqlProfileExecutor();
        using var source = new Subject<SourceEvent>();
        var sourceException = new InvalidOperationException("source stopped");
        Exception? observedException = null;
        using var completed = new ManualResetEventSlim(false);
        IDisposable? subscription = null;

        subscription = executor
            .Execute(source, CreatePassThroughProfile(), TestContext.CancellationToken)
            .Subscribe(
                _ => { },
                error => {
                    observedException = error;
                    subscription?.Dispose();
                    completed.Set();
                },
                completed.Set);

        source.OnError(sourceException);

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5), TestContext.CancellationToken), "The output observer did not receive source termination.");
        Assert.AreSame(sourceException, observedException);
    }

    [TestMethod]
    public void Execute_CanBeCreatedConcurrentlyWithoutReregisteringScalarFunctions()
    {
        var exceptions = new List<Exception>();

        Parallel.For(0, 64, _ => {
            try
            {
                using var executor = new ResourceKqlProfileExecutor();
                using var subscription = executor
                    .Execute(Observable.Empty<SourceEvent>(), CreatePassThroughProfile(), TestContext.CancellationToken)
                    .Subscribe(_ => { });
            }
            catch (Exception ex)
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        });

        Assert.IsEmpty(exceptions, string.Join(Environment.NewLine, exceptions));
    }

    [TestMethod]
    public void NormalizeQueryForRxKql_RewritesNotInAliasOutsideStrings()
    {
        const string query = "EventLog | where EventID notin (4656, 4658) | where Message has 'notin' | where Other NOTIN (1)";

        var result = ResourceKqlProfileExecutor.NormalizeQueryForRxKql(query);

        Assert.AreEqual("EventLog | where EventID !in (4656, 4658) | where Message has 'notin' | where Other !in (1)", result);
    }

    [TestMethod]
    public void NormalizeQueryForRxKql_RewritesHasAnyOutsideStrings()
    {
        const string query = "Source | where Message has_any (\"Failed password\", \"Accepted password\") or EventName HAS_ANY (\"Connect\", \"Endpoint\") | project Message";

        var result = ResourceKqlProfileExecutor.NormalizeQueryForRxKql(query);

        Assert.AreEqual("Source | where (Message has \"Failed password\" or Message has \"Accepted password\") or (EventName has \"Connect\" or EventName has \"Endpoint\") | project Message", result);
    }

    [TestMethod]
    public void NormalizeQueryForRxKql_DoesNotRewriteHasAnyInsideStrings()
    {
        const string query = "Source | where Message == 'has_any (\"x\")' | project Message";

        var result = ResourceKqlProfileExecutor.NormalizeQueryForRxKql(query);

        Assert.AreEqual(query, result);
    }

    [TestMethod]
    public void NormalizeQueryForRxKql_DoesNotRewriteIdentifierSubstrings()
    {
        const string query = "EventLog | where Annotationnotin == 'x' | where notinValue == 'notin'";

        var result = ResourceKqlProfileExecutor.NormalizeQueryForRxKql(query);

        Assert.AreEqual(query, result);
    }

    [TestMethod]
    public void NormalizeQueryForRxKql_RewritesInputTableToSourceObservableOutsideStrings()
    {
        const string query = "EventLog | where source =~ 'EventLog' | project EventId, EventLogValue";

        var result = ResourceKqlProfileExecutor.NormalizeQueryForRxKql(query, "EventLog", "Source");

        Assert.AreEqual("Source | where source =~ 'EventLog' | project EventId, EventLogValue", result);
    }

    [TestMethod]
    public void SourceEventToKqlRow_ExposesNestedWindowsEventDataAndMetadata()
    {
        var source = new SourceEvent(
            new ResourceMetadata {
                CollectorId = "agent-01",
                ProfileId = "windows.security.4688",
                SourceType = "WindowsEventLog",
                SourceName = "Security",
                Platform = "windows",
                Hostname = "host01"
            },
            new Dictionary<string, object?> {
                ["EventId"] = 4688,
                ["EventData"] = new Dictionary<string, object?> {
                    ["TargetUserSid"] = "S-1-5-21-test",
                    ["NewProcessName"] = "C:\\Windows\\System32\\cmd.exe"
                }
            });

        var row = DictionaryCoercion.ToKqlDictionary(source.ToKqlRow());

        Assert.AreEqual(4688, row["EventId"]);
        Assert.AreEqual("Security", row["source"]);

        var eventData = AssertDictionary(row["EventData"]);
        Assert.AreEqual("S-1-5-21-test", eventData["TargetUserSid"]);
        Assert.AreEqual("C:\\Windows\\System32\\cmd.exe", eventData["NewProcessName"]);

        var metadata = AssertDictionary(row["_metadata"]);
        Assert.AreEqual("windows.security.4688", metadata["profileId"]);
        Assert.AreEqual("WindowsEventLog", metadata["sourceType"]);
        Assert.AreEqual("Security", metadata["sourceName"]);
    }

    [TestMethod]
    public void SourceEventToKqlRow_ExposesNestedAuditdRecordsForKqlProbe()
    {
        var source = new SourceEvent(
            new ResourceMetadata {
                CollectorId = "agent-01",
                SourceType = "LinuxAuditd",
                SourceName = "audit.log",
                Platform = "linux",
                Hostname = "host01"
            },
            new Dictionary<string, object?> {
                ["SYSCALL"] = new Dictionary<string, object?> {
                    ["SYSCALL"] = "execve",
                    ["success"] = "yes"
                },
                ["EXECVE"] = new Dictionary<string, object?> {
                    ["ARGV"] = new[] { "/usr/bin/curl", "-s", "https://example.com" }
                }
            });

        var row = DictionaryCoercion.ToKqlDictionary(source.ToKqlRow());

        Assert.AreEqual("audit.log", row["source"]);

        var syscall = AssertDictionary(row["SYSCALL"]);
        Assert.AreEqual("execve", syscall["SYSCALL"]);

        var execve = AssertDictionary(row["EXECVE"]);
        var argv = execve["ARGV"] as string[];
        Assert.IsNotNull(argv);
        CollectionAssert.AreEqual(new[] { "/usr/bin/curl", "-s", "https://example.com" }, argv);
    }

    [TestMethod]
    public void SourceEventToKqlRow_PreservesExistingSourceField()
    {
        var source = new SourceEvent(
            new ResourceMetadata { SourceName = "Security" },
            new Dictionary<string, object?> { ["source"] = "ExplicitSource" });

        var row = DictionaryCoercion.ToKqlDictionary(source.ToKqlRow());

        Assert.AreEqual("ExplicitSource", row["source"]);
    }


    [TestMethod]
    public void Execute_DocumentedAuditdLaurelStyleQueryRunsOnAgentKqlSubset()
    {
        using var executor = new ResourceKqlProfileExecutor();
        var source = new SourceEvent(
            new ResourceMetadata { SourceType = "LinuxAuditd", SourceName = "auditd", Platform = "linux", Hostname = "host01" },
            new Dictionary<string, object?> {
                ["SYSCALL"] = new Dictionary<string, object?> {
                    ["SYSCALL"] = "execve",
                    ["syscall"] = 59,
                    ["UID"] = "1000",
                    ["exe"] = "/usr/bin/curl",
                    ["key"] = "network"
                },
                ["EXECVE"] = new Dictionary<string, object?> {
                    ["ARGV"] = new[] { "/usr/bin/curl", "-s", "https://example.com" }
                },
                ["PROCTITLE"] = new Dictionary<string, object?> {
                    ["ARGV"] = new[] { "/usr/bin/curl", "-s", "https://example.com" }
                }
            });

        var profile = CreatePassThroughProfile();
        profile.Input.Table = "EventLog";
        profile.Filter.Query = """
            EventLog
            | where source =~ "auditd"
            | where SYSCALL.SYSCALL == "execve" or SYSCALL.syscall == 59
            | extend user = tostring(SYSCALL.UID)
            | extend exe = tostring(SYSCALL.exe)
            | extend audit_key = tostring(SYSCALL.key)
            | extend argv_from_execve = strcat_array(EXECVE.ARGV, " ")
            | extend argv_from_proctitle = strcat_array(PROCTITLE.ARGV, " ")
            | extend full_cli = iff(isnotempty(argv_from_proctitle), argv_from_proctitle, argv_from_execve)
            | project user, exe, audit_key, full_cli
            """;

        var rows = executor.Execute(Observable.Return(source), profile, TestContext.CancellationToken)
            .Timeout(TimeSpan.FromSeconds(5))
            .ToList()
            .Wait();

        Assert.HasCount(1, rows);
        Assert.AreEqual("1000", rows[0].Event["user"]);
        Assert.AreEqual("/usr/bin/curl", rows[0].Event["exe"]);
        Assert.AreEqual("network", rows[0].Event["audit_key"]);
        Assert.AreEqual("/usr/bin/curl -s https://example.com", rows[0].Event["full_cli"]);
    }

    private static ResourceProfile CreatePassThroughProfile() => new() {
        SchemaVersion = 1,
        Id = "test.pass-through",
        Name = "Pass through",
        Version = "1.0.0",
        Resource = new ResourceDescriptor { Platform = "test", Family = "test" },
        Input = new ResourceInputContract { Table = "Source", Schema = "Value:string" },
        Filter = new ResourceFilter { Language = "kql", Query = "Source" },
        Output = new ResourceOutputContract { Format = "ndjson", PreserveOriginalFieldNames = true }
    };

    private static IReadOnlyDictionary<string, object?> AssertDictionary(object value)
    {
        if (value is IReadOnlyDictionary<string, object?> dictionary)
        {
            return dictionary;
        }

        Assert.Fail($"Expected a nested dictionary but found {value.GetType().FullName}.");
        return new Dictionary<string, object?>();
    }

    public TestContext TestContext { get; set; }
}