using System.Dynamic;
using System.Text;
using DeltaZulu.Pipeline.Core.Relp;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class CoreTests
{
    [TestMethod]
    public void CoerceToNullableDictionary_ReturnsNullForNull() => Assert.IsNull(DictionaryCoercion.CoerceToNullableDictionary(null));

    [TestMethod]
    public void CoerceToNullableDictionary_CopiesDictionariesWithCaseInsensitiveKeys()
    {
        var source = new Dictionary<string, object?> { ["Name"] = "alice" };

        var result = DictionaryCoercion.CoerceToNullableDictionary(source);

        Assert.AreNotSame(source, result);
        Assert.AreEqual("alice", result!["name"]);
    }

    [TestMethod]
    public void CoerceToNullableDictionary_ExpandoObjectBecomesDictionary()
    {
        dynamic expando = new ExpandoObject();
        expando.Count = 3;

        var result = DictionaryCoercion.CoerceToNullableDictionary(expando);

        Assert.AreEqual(3, result!["count"]);
    }

    [TestMethod]
    public void ToKqlDictionary_DropsNullValuesAndKeepsNonNullValues()
    {
        var result = DictionaryCoercion.ToKqlDictionary(new Dictionary<string, object?> {
            ["keep"] = 42,
            ["drop"] = null
        });

        Assert.AreEqual(42, result["KEEP"]);
        Assert.IsFalse(result.ContainsKey("drop"));
    }

    [TestMethod]
    public async Task ReadFrameAsync_ReturnsNullWhenStreamEndsBeforePayloadCompletes()
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes("1 syslog 5 abc"));

        var result = await RelpFrameCodec.ReadFrameAsync(stream, TestContext.CancellationToken);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task ReadFrameAsync_ReturnsNullWhenStreamEndsBeforeTerminator()
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes("1 syslog 3 abc"));

        var result = await RelpFrameCodec.ReadFrameAsync(stream, TestContext.CancellationToken);

        Assert.IsNull(result);
    }

    public TestContext TestContext { get; set; }
}
