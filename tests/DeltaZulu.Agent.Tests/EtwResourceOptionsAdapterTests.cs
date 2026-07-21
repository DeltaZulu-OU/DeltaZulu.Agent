using DeltaZulu.Pipeline.Core.Profiles;
using DeltaZulu.Pipeline.Inputs.Etw;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class EtwResourceOptionsAdapterTests
{
    [TestMethod]
    public void Adapt_ReturnsDefaultsWhenOptionsBagIsEmpty()
    {
        var resource = new ResourceDescriptor { Platform = "windows", Family = "etw" };

        var options = new EtwResourceOptionsAdapter().Adapt(resource);

        Assert.IsEmpty(options.EventIds);
        Assert.IsEmpty(options.ExcludedEventIds);
        Assert.IsEmpty(options.Opcodes);
        Assert.IsEmpty(options.Versions);
        Assert.IsFalse(options.CaptureStacks);
        Assert.IsEmpty(options.ProcessIds);
        Assert.IsEmpty(options.ProcessNames);
        Assert.IsEmpty(options.PayloadFields);
        Assert.IsFalse(options.EnableInContainers);
        Assert.IsFalse(options.EnableSourceContainerTracking);
    }

    [TestMethod]
    public void Adapt_UnwrapsBoxedListsAndScalarsFromOptionsBag()
    {
        // Mirrors the shape YamlDotNet produces for an untyped mapping: sequences become
        // List<object> of boxed scalars, plain scalars are boxed directly.
        var resource = new ResourceDescriptor {
            Platform = "windows",
            Family = "etw",
            Options = new Dictionary<string, object?> {
                ["eventIds"] = new List<object?> { 1, 2 },
                ["excludedEventIds"] = new List<object?> { 3 },
                ["opcodes"] = new List<object?> { 10 },
                ["versions"] = new List<object?> { 1 },
                ["captureStacks"] = true,
                ["stackEventIds"] = new List<object?> { 4 },
                ["excludedStackEventIds"] = new List<object?> { 5 },
                ["processIds"] = new List<object?> { 1234 },
                ["processNames"] = new List<object?> { "notepad.exe" },
                ["payloadFields"] = new List<object?> { "Image", "CommandLine" },
                ["enableInContainers"] = true,
                ["enableSourceContainerTracking"] = true
            }
        };

        var options = new EtwResourceOptionsAdapter().Adapt(resource);

        Assert.AreSequenceEqual(new[] { 1, 2 }, options.EventIds);
        Assert.AreSequenceEqual(new[] { 3 }, options.ExcludedEventIds);
        Assert.AreSequenceEqual(new[] { 10 }, options.Opcodes);
        Assert.AreSequenceEqual(new[] { 1 }, options.Versions);
        Assert.IsTrue(options.CaptureStacks);
        Assert.AreSequenceEqual(new[] { 4 }, options.StackEventIds);
        Assert.AreSequenceEqual(new[] { 5 }, options.ExcludedStackEventIds);
        Assert.AreSequenceEqual(new[] { 1234 }, options.ProcessIds);
        Assert.AreSequenceEqual(new[] { "notepad.exe" }, options.ProcessNames);
        Assert.AreSequenceEqual(new[] { "Image", "CommandLine" }, options.PayloadFields);
        Assert.IsTrue(options.EnableInContainers);
        Assert.IsTrue(options.EnableSourceContainerTracking);
    }

    [TestMethod]
    public void Adapt_AcceptsSingleScalarInPlaceOfList()
    {
        var resource = new ResourceDescriptor {
            Platform = "windows",
            Family = "etw",
            Options = new Dictionary<string, object?> {
                ["eventIds"] = 42,
                ["processNames"] = "notepad.exe",
                ["payloadFields"] = "Image"
            }
        };

        var options = new EtwResourceOptionsAdapter().Adapt(resource);

        Assert.AreSequenceEqual(new[] { 42 }, options.EventIds);
        Assert.AreSequenceEqual(new[] { "notepad.exe" }, options.ProcessNames);
        Assert.AreSequenceEqual(new[] { "Image" }, options.PayloadFields);
    }

    [TestMethod]
    public void BuildSelectedPayloadFields_ReturnsCaseInsensitiveProjectionSet()
    {
        var resource = new ResourceDescriptor {
            Platform = "windows",
            Family = "etw",
            Options = new Dictionary<string, object?> {
                ["payloadFields"] = new List<object?> { "Image", "CommandLine" }
            }
        };

        var fields = EtwPayloadProjection.BuildSelectedPayloadFields(resource);

        Assert.IsNotNull(fields);
        Assert.Contains("image", fields);
        Assert.Contains("COMMANDLINE", fields);
        Assert.DoesNotContain("ParentImage", fields);
    }

    [TestMethod]
    public void BuildSelectedPayloadFields_ReturnsNullWhenProjectionIsNotConfigured()
    {
        var resource = new ResourceDescriptor { Platform = "windows", Family = "etw" };

        var fields = EtwPayloadProjection.BuildSelectedPayloadFields(resource);

        Assert.IsNull(fields);
    }

    [TestMethod]
    public void SelectPayloadNames_ReturnsEnvelopeIndependentProjectionOnly()
    {
        var payloadNames = new[] { "Image", "CommandLine", "ParentImage" };
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image", "commandline" };

        var materializedNames = EtwPayloadProjection.SelectPayloadNames(payloadNames, selected).ToArray();

        Assert.AreSequenceEqual(new[] { "Image", "CommandLine" }, materializedNames);
        Assert.IsFalse(materializedNames.Contains("ParentImage"));
    }

    [TestMethod]
    public void SelectPayloadNames_ReturnsAllPayloadNamesWhenProjectionIsNotConfigured()
    {
        var payloadNames = new[] { "Image", "CommandLine", "ParentImage" };

        var materializedNames = EtwPayloadProjection.SelectPayloadNames(payloadNames, selectedPayloadFields: null).ToArray();

        Assert.AreSequenceEqual(payloadNames, materializedNames);
    }

    [TestMethod]
    public void SelectMissingPayloadFields_UsesPayloadOnlyMaterializationResult()
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ProviderName", "Image", "CommandLine", "Broken" };
        var materialization = new EtwPayloadMaterializationResult(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Image" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Broken" });

        var missing = EtwPayloadProjection.SelectMissingPayloadFields(selected, materialization).ToArray();

        Assert.AreSequenceEqual(new[] { "ProviderName", "CommandLine" }, missing);
        Assert.IsFalse(missing.Contains("Image"));
        Assert.IsFalse(missing.Contains("Broken"));
    }
}
