using System.Xml.Linq;

namespace DeltaZulu.Pipeline.Inputs.Windows;

/// <summary>
/// Parses a Windows provider manifest template (the XML fragment exposed by
/// <c>System.Diagnostics.Eventing.Reader.EventMetadata.Template</c>) into field schema entries.
/// Host-neutral and pure so it can be unit-tested without a Windows host.
/// </summary>
/// <remarks>
/// A template looks like <c>&lt;template&gt;&lt;data name="X" inType="win:SID"/&gt;...&lt;/template&gt;</c>.
/// Elements are matched by local name so a default manifest namespace does not defeat extraction;
/// field order is preserved.
/// </remarks>
public static class WindowsEventTemplateParser
{
    public static IReadOnlyList<WindowsEventFieldSchema> ExtractFields(string? templateXml)
    {
        if (string.IsNullOrWhiteSpace(templateXml))
        {
            return [];
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(templateXml, LoadOptions.None);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var fields = new List<WindowsEventFieldSchema>();
        foreach (var data in document.Descendants().Where(e => e.Name.LocalName == "data"))
        {
            var name = data.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            fields.Add(new WindowsEventFieldSchema {
                Name = name,
                Type = data.Attribute("inType")?.Value
            });
        }

        return fields;
    }
}
