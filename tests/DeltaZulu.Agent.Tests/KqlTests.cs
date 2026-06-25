using System.Reactive.Linq;
using DeltaZulu.Agent.Core.Events;
using DeltaZulu.Agent.Kql;
using DeltaZulu.Agent.Profiles;

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
            .Execute(Observable.Throw<SourceEvent>(sourceException), CreatePassThroughProfile())
            .Subscribe(
                _ => { },
                error => {
                    observedException = error;
                    completed.Set();
                },
                completed.Set);

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)), "The output observer did not receive source termination.");
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
                    .Execute(Observable.Empty<SourceEvent>(), CreatePassThroughProfile())
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

        Assert.AreEqual(0, exceptions.Count, string.Join(Environment.NewLine, exceptions));
    }

    [TestMethod]
    public void NormalizeQueryForRxKql_RewritesNotInAliasOutsideStrings()
    {
        var query = "EventLog | where EventID notin (4656, 4658) | where Message has 'notin' | where Other NOTIN (1)";

        var result = ResourceKqlProfileExecutor.NormalizeQueryForRxKql(query);

        Assert.AreEqual("EventLog | where EventID !in (4656, 4658) | where Message has 'notin' | where Other !in (1)", result);
    }

    [TestMethod]
    public void NormalizeQueryForRxKql_DoesNotRewriteIdentifierSubstrings()
    {
        var query = "EventLog | where Annotationnotin == 'x' | where notinValue == 'notin'";

        var result = ResourceKqlProfileExecutor.NormalizeQueryForRxKql(query);

        Assert.AreEqual(query, result);
    }

    private static ResourceProfile CreatePassThroughProfile() => new()
    {
        SchemaVersion = 1,
        Id = "test.pass-through",
        Name = "Pass through",
        Version = "1.0.0",
        Resource = new ResourceDescriptor { Platform = "test", Family = "test" },
        Input = new ResourceInputContract { Table = "Source", Schema = "Value:string" },
        Filter = new ResourceFilter { Language = "kql", Query = "Source | take 1" },
        Output = new ResourceOutputContract { Format = "ndjson", PreserveOriginalFieldNames = true }
    };
}
