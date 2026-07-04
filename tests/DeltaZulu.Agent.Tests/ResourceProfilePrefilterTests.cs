using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Profiles;
using DeltaZulu.Agent.Filter.Prefilter;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ResourceProfilePrefilterTests
{
    [TestMethod]
    public void IsSatisfied_ReturnsTrueWhenProfileHasNoCondition()
    {
        var prefilter = new ResourceProfilePrefilter([]);

        var satisfied = prefilter.IsSatisfied(CreateProfile(condition: null), out var warning);

        Assert.IsTrue(satisfied);
        Assert.IsNull(warning);
    }

    [TestMethod]
    public void IsSatisfied_ReturnsTrueWhenEvaluatorReportsSatisfied()
    {
        var prefilter = new ResourceProfilePrefilter([new FakeEvaluator("fake", satisfied: true)]);
        var profile = CreateProfile(new ResourceCondition { Type = "fake", Query = "q" });

        var satisfied = prefilter.IsSatisfied(profile, out var warning);

        Assert.IsTrue(satisfied);
        Assert.IsNull(warning);
    }

    [TestMethod]
    public void IsSatisfied_WarnsAndReturnsFalseWhenOptionalConditionIsNotSatisfied()
    {
        var prefilter = new ResourceProfilePrefilter([new FakeEvaluator("fake", satisfied: false)]);
        var profile = CreateProfile(new ResourceCondition { Type = "fake", Query = "q", Mandatory = false });

        var satisfied = prefilter.IsSatisfied(profile, out var warning);

        Assert.IsFalse(satisfied);
        Assert.IsNotNull(warning);
    }

    [TestMethod]
    public void IsSatisfied_ThrowsWhenMandatoryConditionIsNotSatisfied()
    {
        var prefilter = new ResourceProfilePrefilter([new FakeEvaluator("fake", satisfied: false)]);
        var profile = CreateProfile(new ResourceCondition { Type = "fake", Query = "q", Mandatory = true });

        Assert.ThrowsExactly<InvalidOperationException>(() => prefilter.IsSatisfied(profile, out _));
    }

    [TestMethod]
    public void IsSatisfied_WarnsWhenNoEvaluatorIsRegisteredForOptionalCondition()
    {
        var prefilter = new ResourceProfilePrefilter([]);
        var profile = CreateProfile(new ResourceCondition { Type = "unregistered", Query = "q", Mandatory = false });

        var satisfied = prefilter.IsSatisfied(profile, out var warning);

        Assert.IsFalse(satisfied);
        Assert.IsNotNull(warning);
        Assert.Contains("no registered evaluator", warning);
    }

    [TestMethod]
    public void IsSatisfied_ThrowsWhenNoEvaluatorIsRegisteredForMandatoryCondition()
    {
        var prefilter = new ResourceProfilePrefilter([]);
        var profile = CreateProfile(new ResourceCondition { Type = "unregistered", Query = "q", Mandatory = true });

        Assert.ThrowsExactly<InvalidOperationException>(() => prefilter.IsSatisfied(profile, out _));
    }

    [TestMethod]
    public void IsSatisfied_ThrowsWhenMandatoryConditionCannotBeEvaluated()
    {
        var prefilter = new ResourceProfilePrefilter([FakeEvaluator.ThatFailsToEvaluate("fake")]);
        var profile = CreateProfile(new ResourceCondition { Type = "fake", Query = "q", Mandatory = true });

        Assert.ThrowsExactly<InvalidOperationException>(() => prefilter.IsSatisfied(profile, out _));
    }

    private static ResourceProfile CreateProfile(ResourceCondition? condition) => new() {
        SchemaVersion = 1,
        Id = "test.profile",
        Name = "Test",
        Version = "1.0.0",
        Resource = new ResourceDescriptor { Platform = "linux", Family = "syslog" },
        Input = new ResourceInputContract { Table = "Source", Schema = "RawMessage:string" },
        Filter = new ResourceFilter { Language = "kql", Query = "Source | take 1" },
        Output = new ResourceOutputContract { Format = "ndjson", PreserveOriginalFieldNames = true },
        Condition = condition
    };

    private sealed class FakeEvaluator(string type, bool satisfied, bool canEvaluate = true) : IResourceConditionEvaluator
    {
        public static FakeEvaluator ThatFailsToEvaluate(string type) => new(type, satisfied: false, canEvaluate: false);

        public bool Handles(string conditionType) => conditionType.Equals(type, StringComparison.OrdinalIgnoreCase);

        public bool TryEvaluate(ResourceCondition condition, out bool isSatisfied, out Exception? error)
        {
            if (!canEvaluate)
            {
                isSatisfied = false;
                error = new InvalidOperationException("simulated evaluation failure");
                return false;
            }

            isSatisfied = satisfied;
            error = null;
            return true;
        }
    }
}
