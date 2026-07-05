using DeltaZulu.Pipeline.Inputs.Common;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class LogFieldNormalizerTests
{
    [TestMethod]
    public void ParseKeyValueFields_NormalizesQuotedEscapedAndScalarValues()
    {
        var fields = LogFieldNormalizer.ParseKeyValueFields(@"user=""alice smith"" path=""/tmp/a\\"" retries=3 success=true ignored-token latency=12.5");

        Assert.AreEqual("alice smith", fields["user"]);
        Assert.AreEqual(@"/tmp/a\", fields["path"]);
        Assert.AreEqual(3L, fields["retries"]);
        Assert.AreEqual(true, fields["success"]);
        Assert.AreEqual(12.5m, fields["latency"]);
        Assert.IsFalse(fields.ContainsKey("ignored-token"));
    }

    [TestMethod]
    public void ParseDelimitedFields_MapsW3cStyleRowsToNamedDictionary()
    {
        var names = new[] { "date", "time", "s-ip", "cs-method", "cs-uri-stem", "sc-status", "cs(User-Agent)" };
        var fields = LogFieldNormalizer.ParseDelimitedFields(
            names,
            @"2026-07-05 12:00:01 10.0.0.5 GET /index.html 200 ""Mozilla/5.0 Test""");

        Assert.AreEqual("2026-07-05", fields["date"]);
        Assert.AreEqual("12:00:01", fields["time"]);
        Assert.AreEqual("10.0.0.5", fields["s-ip"]);
        Assert.AreEqual("GET", fields["cs-method"]);
        Assert.AreEqual("/index.html", fields["cs-uri-stem"]);
        Assert.AreEqual(200L, fields["sc-status"]);
        Assert.AreEqual("Mozilla/5.0 Test", fields["cs(User-Agent)"]);
    }
}
