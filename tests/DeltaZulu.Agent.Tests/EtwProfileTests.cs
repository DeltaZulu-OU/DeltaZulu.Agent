using System.Reactive.Linq;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;
using DeltaZulu.Agent.Filter.Kql;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class EtwProfileTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void KernelProcessProfile_Validates_WithoutErrors()
    {
        var profile = CreateKernelProcessProfile();

        var errors = new ResourceProfileValidator().Validate(profile);

        Assert.IsEmpty(errors, $"Profile validation errors: {string.Join("; ", errors)}");
    }

    [TestMethod]
    public void KernelProcessProfile_WhereProviderNameEquals_ProducesOutput()
    {
        using var executor = new ResourceKqlProfileExecutor();
        var profile = CreateProfileWithQuery("""Etw | where ProviderName == "Microsoft-Windows-Kernel-Process" """);
        var mockEtwEvent = CreateMockEtwEvent();

        var (captured, capturedError) = ExecuteSingle(executor, mockEtwEvent, profile);

        Assert.IsNull(capturedError, $"Query raised an exception: {capturedError}");
        Assert.IsNotNull(captured, "ProviderName == filter should match the mock event");
    }

    [TestMethod]
    public void KernelProcessProfile_WhereSourceCaseInsensitive_ProducesOutput()
    {
        using var executor = new ResourceKqlProfileExecutor();
        var profile = CreateProfileWithQuery("""Etw | where source =~ "Microsoft-Windows-Kernel-Process" """);
        var mockEtwEvent = CreateMockEtwEvent();

        var (captured, capturedError) = ExecuteSingle(executor, mockEtwEvent, profile);

        Assert.IsNull(capturedError, $"Query raised an exception: {capturedError}");
        Assert.IsNotNull(captured, "source =~ filter should match the mock event");
    }

    [TestMethod]
    public void KernelProcessProfile_FullFilterQuery_ProducesOutput()
    {
        using var executor = new ResourceKqlProfileExecutor();
        var profile = CreateKernelProcessProfile();
        var mockEtwEvent = CreateMockEtwEvent();

        var (captured, capturedError) = ExecuteSingle(executor, mockEtwEvent, profile);

        Assert.IsNull(capturedError, $"Query raised an exception: {capturedError}");
        Assert.IsNotNull(captured, "Full ETW filter query should produce output for matching provider");
    }

    [TestMethod]
    public void KernelProcessProfile_RejectsEventsFromDifferentProvider()
    {
        using var executor = new ResourceKqlProfileExecutor();
        var profile = CreateKernelProcessProfile();
        var mockEtwEvent = CreateMockEtwEventWithProvider("Microsoft-Windows-Other-Provider");

        var recordsReceived = new List<ResourceOutputRecord>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);

        using var subscription = executor
            .Execute(Observable.Return(mockEtwEvent), profile, cts.Token)
            .Subscribe(record => recordsReceived.Add(record));

        Thread.Sleep(500);
        cts.Cancel();

        Assert.IsEmpty(recordsReceived, "Events from different providers should be filtered out");
    }

    [TestMethod]
    public void KernelProcessProfile_PreservesEtwDeliveryMetadata()
    {
        using var executor = new ResourceKqlProfileExecutor();
        var profile = CreateKernelProcessProfile();
        var mockEtwEvent = CreateMockEtwEvent();

        var (captured, capturedError) = ExecuteSingle(executor, mockEtwEvent, profile);

        Assert.IsNull(capturedError, $"Query raised an exception: {capturedError}");
        Assert.IsNotNull(captured);
        Assert.AreEqual("windows.etw.kernel-process", captured.Metadata["profileId"]);
        Assert.AreEqual("1.0.0", captured.Metadata["profileVersion"]);
        Assert.AreEqual("test-agent", captured.Metadata["collectorId"]);
        Assert.AreEqual("WindowsEtw", captured.Metadata["sourceType"]);
        Assert.AreEqual("Microsoft-Windows-Kernel-Process", captured.Metadata["sourceName"]);
        Assert.AreEqual("windows", captured.Metadata["platform"]);
        Assert.AreEqual("test-host", captured.Metadata["hostname"]);
    }

    [TestMethod]
    public void KernelProcessProfile_ProjectProviderAndEventId_EmitsSelectedEtwFields()
    {
        using var executor = new ResourceKqlProfileExecutor();
        var profile = CreateProfileWithQuery(
            """
            Etw
            | where ProviderName == "Microsoft-Windows-Kernel-Process"
            | project ProviderName, EventId, EventName
            """);
        var mockEtwEvent = CreateMockEtwEvent();

        var (captured, capturedError) = ExecuteSingle(executor, mockEtwEvent, profile);

        Assert.IsNull(capturedError, $"Query raised an exception: {capturedError}");
        Assert.IsNotNull(captured);
        Assert.AreEqual("Microsoft-Windows-Kernel-Process", captured.Event["ProviderName"]);
        Assert.AreEqual(1, Convert.ToInt32(captured.Event["EventId"]));
        Assert.AreEqual("ProcessCreate", captured.Event["EventName"]);
        Assert.IsFalse(captured.Event.ContainsKey("RawEvent"), "Projected ETW output should not include unselected raw bytes.");
    }

    [TestMethod]
    public void KernelProcessProfile_WhereEventIdNotIn_RejectsExcludedEtwEventIds()
    {
        using var executor = new ResourceKqlProfileExecutor();
        var profile = CreateProfileWithQuery(
            """
            Etw
            | where ProviderName == "Microsoft-Windows-Kernel-Process"
            | where EventId notin (2, 3)
            """);
        var rejectedEtwEvent = CreateMockEtwEvent(eventId: 2);

        var (captured, capturedError) = ExecuteSingle(executor, rejectedEtwEvent, profile);

        Assert.IsNull(capturedError, $"Query raised an exception: {capturedError}");
        Assert.IsNull(captured, "EventId notin filter should reject excluded ETW event IDs.");
    }

    [TestMethod]
    public void KernelProcessProfile_WhereEventIdNotIn_AllowsUnlistedEtwEventIds()
    {
        using var executor = new ResourceKqlProfileExecutor();
        var profile = CreateProfileWithQuery(
            """
            Etw
            | where ProviderName == "Microsoft-Windows-Kernel-Process"
            | where EventId notin (2, 3)
            """);
        var acceptedEtwEvent = CreateMockEtwEvent(eventId: 1);

        var (captured, capturedError) = ExecuteSingle(executor, acceptedEtwEvent, profile);

        Assert.IsNull(capturedError, $"Query raised an exception: {capturedError}");
        Assert.IsNotNull(captured, "EventId notin filter should allow unlisted ETW event IDs.");
    }

    [TestMethod]
    public void Execute_PropagatesSourceErrorsAfterReorder()
    {
        using var executor = new ResourceKqlProfileExecutor();
        var sourceException = new UnauthorizedAccessException("denied");
        Exception? observedException = null;
        using var completed = new ManualResetEventSlim(false);

        using var subscription = executor
            .Execute(Observable.Throw<SourceEvent>(sourceException), CreateProfileWithQuery("Etw"), TestContext.CancellationToken)
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

    private (ResourceOutputRecord? Record, Exception? Error) ExecuteSingle(
        ResourceKqlProfileExecutor executor,
        SourceEvent sourceEvent,
        ResourceProfile profile)
    {
        ResourceOutputRecord? captured = null;
        Exception? capturedError = null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        using var signal = new ManualResetEventSlim(false);

        using var subscription = executor
            .Execute(Observable.Return(sourceEvent), profile, cts.Token)
            .Subscribe(
                record => { captured = record; signal.Set(); },
                error => { capturedError = error; signal.Set(); });

        signal.Wait(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        cts.Cancel();

        return (captured, capturedError);
    }

    private static ResourceProfile CreateProfileWithQuery(string query) => new() {
        SchemaVersion = 1,
        Id = "windows.etw.kernel-process",
        Name = "Windows Kernel Process ETW resource filter",
        Version = "1.0.0",
        Enabled = true,
        Resource = new ResourceDescriptor {
            Platform = "windows",
            Family = "etw",
            Mode = "managed",
            Session = "DeltaZulu-Kernel-Process",
            Provider = "Microsoft-Windows-Kernel-Process"
        },
        Input = new ResourceInputContract {
            Table = "Etw",
            Schema = "WindowsEtw.Native"
        },
        Filter = new ResourceFilter {
            Language = "kql",
            Query = query
        },
        Output = new ResourceOutputContract {
            Format = "ndjson",
            PreserveOriginalFieldNames = true
        }
    };

    private static ResourceProfile CreateKernelProcessProfile() => CreateProfileWithQuery(
        """
        Etw
        | where ProviderName =~ "Microsoft-Windows-Kernel-Process"
        """);

    private static SourceEvent CreateMockEtwEvent(int eventId = 1) =>
        CreateMockEtwEventWithProvider("Microsoft-Windows-Kernel-Process", eventId);

    private static SourceEvent CreateMockEtwEventWithProvider(string providerName, int eventId = 1) => new(
        new ResourceMetadata {
            CollectorId = "test-agent",
            ProfileId = "windows.etw.kernel-process",
            SourceType = "WindowsEtw",
            SourceName = providerName,
            Platform = "windows",
            Hostname = "test-host"
        },
        new Dictionary<string, object?> {
            ["Timestamp"] = DateTime.UtcNow,
            ["ProviderName"] = providerName,
            ["source"] = providerName,
            ["EventId"] = eventId,
            ["EventName"] = "ProcessCreate",
            ["Opcode"] = 1,
            ["Task"] = 1,
            ["Keywords"] = 0x8000000000000000L,
            ["ActivityId"] = Guid.NewGuid(),
            ["Payload"] = new Dictionary<string, object?> {
                ["ProcessId"] = 1234,
                ["ImageFileName"] = "notepad.exe"
            },
            ["RawEvent"] = Array.Empty<byte>()
        });
}