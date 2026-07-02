using DeltaZulu.Pipeline.Core;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class PollRetryPolicyTests
{
    [TestMethod]
    public void GetBackoffDelay_ZeroOrNegativeFailures_ReturnsZero()
    {
        var policy = new PollRetryPolicy();

        Assert.AreEqual(TimeSpan.Zero, policy.GetBackoffDelay(0));
        Assert.AreEqual(TimeSpan.Zero, policy.GetBackoffDelay(-3));
    }

    [TestMethod]
    public void GetBackoffDelay_GrowsExponentiallyFromBaseDelay()
    {
        var policy = new PollRetryPolicy
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(60)
        };

        Assert.AreEqual(TimeSpan.FromSeconds(1), policy.GetBackoffDelay(1));
        Assert.AreEqual(TimeSpan.FromSeconds(2), policy.GetBackoffDelay(2));
        Assert.AreEqual(TimeSpan.FromSeconds(4), policy.GetBackoffDelay(3));
        Assert.AreEqual(TimeSpan.FromSeconds(8), policy.GetBackoffDelay(4));
    }

    [TestMethod]
    public void GetBackoffDelay_CapsAtMaxDelay()
    {
        var policy = new PollRetryPolicy
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(10)
        };

        // 2^4 = 16s would exceed the 10s cap.
        Assert.AreEqual(TimeSpan.FromSeconds(10), policy.GetBackoffDelay(5));
        // Very large failure counts must not overflow; they saturate at MaxDelay.
        Assert.AreEqual(TimeSpan.FromSeconds(10), policy.GetBackoffDelay(1000));
    }

    [TestMethod]
    public void GetBackoffDelay_ZeroBaseDelay_ReturnsZero()
    {
        var policy = new PollRetryPolicy { BaseDelay = TimeSpan.Zero };

        Assert.AreEqual(TimeSpan.Zero, policy.GetBackoffDelay(3));
    }

    [TestMethod]
    public void ShouldEscalate_TrueOnlyAtOrAboveThreshold()
    {
        var policy = new PollRetryPolicy { MaxConsecutiveFailures = 5 };

        Assert.IsFalse(policy.ShouldEscalate(1));
        Assert.IsFalse(policy.ShouldEscalate(4));
        Assert.IsTrue(policy.ShouldEscalate(5));
        Assert.IsTrue(policy.ShouldEscalate(6));
    }
}
