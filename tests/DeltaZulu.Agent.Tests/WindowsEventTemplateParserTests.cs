using DeltaZulu.Pipeline.Inputs.Windows;

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

        Assert.HasCount(3, fields);
        Assert.AreEqual("SubjectUserSid", fields[0].Name);
        Assert.AreEqual("win:SID", fields[0].Type);
        Assert.AreEqual("TargetUserName", fields[1].Name);
        Assert.AreEqual("LogonType", fields[2].Name);
        // Field order is preserved.
        Assert.AreSequenceEqual(
            new[] { "SubjectUserSid", "TargetUserName", "LogonType" }, fields.Select(f => f.Name).ToArray());
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

        Assert.HasCount(1, fields);
        Assert.AreEqual("Keep", fields[0].Name);
    }

    [TestMethod]
    public void ExtractFields_MalformedOrEmpty_ReturnsEmpty()
    {
        Assert.IsEmpty(WindowsEventTemplateParser.ExtractFields(null));
        Assert.IsEmpty(WindowsEventTemplateParser.ExtractFields(""));
        Assert.IsEmpty(WindowsEventTemplateParser.ExtractFields("<template><data name="));
    }
}
