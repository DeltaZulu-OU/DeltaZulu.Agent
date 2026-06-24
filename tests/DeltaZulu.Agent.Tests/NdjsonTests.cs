using DeltaZulu.Agent.Outputs.Ndjson;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class NdjsonTests
{
    [TestMethod]
    public void CreateDefault_PreservesPropertyNamesAndOmitsNullValues()
    {
        var json = JsonSerializer.Serialize(new { OriginalName = "value", Missing = (string?)null }, NdjsonSerializerOptions.CreateDefault());

        StringAssert.Contains(json, "\"OriginalName\":\"value\"");
        Assert.IsFalse(json.Contains("Missing", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains(Environment.NewLine, StringComparison.Ordinal));
    }

    [TestMethod]
    public void FromException_MapsExceptionDetailsIntoErrorRecord()
    {
        var exception = new InvalidOperationException("boom");

        var record = NdjsonErrorRecord.FromException(exception);

        Assert.AreEqual("resourceql.error", record.Type);
        Assert.AreEqual("boom", record.Message);
        Assert.AreEqual(typeof(InvalidOperationException).FullName, record.ExceptionType);
    }
}
