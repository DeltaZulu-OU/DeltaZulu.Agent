using DeltaZulu.Pipeline.Core.Events;
using System.Dynamic;

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
        var result = DictionaryCoercion.ToKqlDictionary(new Dictionary<string, object?>
        {
            ["keep"] = 42,
            ["drop"] = null
        });

        Assert.AreEqual(42, result["KEEP"]);
        Assert.IsFalse(result.ContainsKey("drop"));
    }
}
