using DeltaZulu.Pipeline.Core.Windows;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class WindowsEventTemplateParserTests
{
    [TestMethod]
    public void ExtractFields_ReadsNamedDataElementsWithTypes_IgnoringNamespace()
    {
        const string template = """
            <template xmlns="http://schemas.microsoft.com/win/2004/08/events">
              <data name="SubjectUserSid" inType="win:SID" outType="xs:string" />
              <data name="TargetUserName" inType="win:UnicodeString" />
              <data name="LogonType" inType="win:UInt32" outType="xs:unsignedInt" />
            </template>
            """;

        var fields = WindowsEventTemplateParser.ExtractFields(template);

        Assert.AreEqual(3, fields.Count);
        Assert.AreEqual("SubjectUserSid", fields[0].Name);
        Assert.AreEqual("win:SID", fields[0].Type);
        Assert.AreEqual("TargetUserName", fields[1].Name);
        Assert.AreEqual("LogonType", fields[2].Name);
        // Field order is preserved.
        CollectionAssert.AreEqual(
            new[] { "SubjectUserSid", "TargetUserName", "LogonType" },
            fields.Select(f => f.Name).ToArray());
    }

    [TestMethod]
    public void ExtractFields_SkipsDataElementsWithoutName()
    {
        const string template = """
            <template xmlns="http://schemas.microsoft.com/win/2004/08/events">
              <data name="Keep" inType="win:UnicodeString" />
              <data inType="win:UInt32" />
            </template>
            """;

        var fields = WindowsEventTemplateParser.ExtractFields(template);

        Assert.AreEqual(1, fields.Count);
        Assert.AreEqual("Keep", fields[0].Name);
    }

    [TestMethod]
    public void ExtractFields_MalformedOrEmpty_ReturnsEmpty()
    {
        Assert.AreEqual(0, WindowsEventTemplateParser.ExtractFields(null).Count);
        Assert.AreEqual(0, WindowsEventTemplateParser.ExtractFields("").Count);
        Assert.AreEqual(0, WindowsEventTemplateParser.ExtractFields("<template><data name=").Count);
    }
}
