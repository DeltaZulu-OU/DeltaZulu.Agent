using System.Reactive.Linq;
using DeltaZulu.Agent.Filter.Kql;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Agent.Tests;

/// <summary>
/// Executable compatibility contract for the pinned Microsoft.Rx.Kql package.
/// Keep each test feature-oriented; this is deliberately not an Agent operator deny list.
/// </summary>
[TestClass]
public sealed class RxKqlCapabilityTests
{
    [DataTestMethod]
    [DataRow("Source", "Value")]
    [DataRow("Source | where Value == 2 and Label has \"ok\"", "Value")]
    [DataRow("Source | extend Twice = Value * 2", "Twice")]
    [DataRow("Source | project Value, Label", "Label")]
    [DataRow("Source | project Renamed = Value", "Renamed")]
    public void RxKqlSupported_SourceFilteringExtensionAndProjection_Execute(string query, string expectedField)
    {
        var rows = Execute(query);

        Assert.HasCount(1, rows);
        Assert.IsTrue(rows[0].Event.ContainsKey(expectedField), $"Expected '{expectedField}' in the Rx.Kql output.");
    }

    [DataTestMethod]
    [DataRow("Source | summarize Total = count()")]
    [DataRow("Source | union Source")]
    [DataRow("Source | join (Source) on Value")]
    public void UnsupportedByPinnedEngine_ReportsRxKqlParsingFailure(string query)
    {
        var failure = Assert.ThrowsExactly<KqlQueryExecutionException>(() => Execute(query));

        Assert.AreEqual(KqlQueryFailureStage.RxKqlParsing, failure.Stage);
        Assert.AreEqual(query, failure.OriginalQuery);
        Assert.AreEqual(query, failure.NormalizedQuery);
    }

    [TestMethod]
    public void AgentCompatibleShims_ExecuteAgainstThePinnedEngine()
    {
        var rows = Execute("EventLog | where Value notin (1) and Label has_any (\"ok\", \"other\") | extend Present = isnotempty(Label)", "EventLog");

        Assert.HasCount(1, rows);
        Assert.AreEqual(true, rows[0].Event["Present"]);
    }

    [TestMethod]
    public void QueryFailureDiagnostics_IdentifyProfileStageAndStructuredQueryText()
    {
        const string query = "Source | union Source";
        var failure = Assert.ThrowsExactly<KqlQueryExecutionException>(() => Execute(query, profileId: "profiles.capability.diagnostic"));

        Assert.AreEqual("profiles.capability.diagnostic", failure.ProfileId);
        Assert.AreEqual(KqlQueryFailureStage.RxKqlParsing, failure.Stage);
        Assert.AreEqual(query, failure.OriginalQuery);
        Assert.AreEqual(query, failure.NormalizedQuery);
        StringAssert.Contains(failure.Message, "profiles.capability.diagnostic");
        Assert.IsFalse(failure.Message.Contains(query, StringComparison.Ordinal));
    }

    private static IList<ResourceOutputRecord> Execute(string query, string table = "Source", string profileId = "rxkql.capability")
    {
        using var executor = new ResourceKqlProfileExecutor();
        var profile = new ResourceProfile {
            Id = profileId,
            Version = "1.0.0",
            Input = new ResourceInputContract { Table = table },
            Filter = new ResourceFilter { Query = query }
        };
        var source = new SourceEvent(
            new ResourceMetadata { SourceName = "capability" },
            new Dictionary<string, object?> { ["Value"] = 2, ["Label"] = "ok" });

        return executor.Execute(Observable.Return(source), profile).ToList().Wait();
    }
}
