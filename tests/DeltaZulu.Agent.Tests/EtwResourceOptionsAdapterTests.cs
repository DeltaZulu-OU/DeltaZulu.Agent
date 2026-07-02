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
                ["enableInContainers"] = true,
                ["enableSourceContainerTracking"] = true
            }
        };

        var options = new EtwResourceOptionsAdapter().Adapt(resource);

        CollectionAssert.AreEqual(new[] { 1, 2 }, options.EventIds);
        CollectionAssert.AreEqual(new[] { 3 }, options.ExcludedEventIds);
        CollectionAssert.AreEqual(new[] { 10 }, options.Opcodes);
        CollectionAssert.AreEqual(new[] { 1 }, options.Versions);
        Assert.IsTrue(options.CaptureStacks);
        CollectionAssert.AreEqual(new[] { 4 }, options.StackEventIds);
        CollectionAssert.AreEqual(new[] { 5 }, options.ExcludedStackEventIds);
        CollectionAssert.AreEqual(new[] { 1234 }, options.ProcessIds);
        CollectionAssert.AreEqual(new[] { "notepad.exe" }, options.ProcessNames);
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
                ["processNames"] = "notepad.exe"
            }
        };

        var options = new EtwResourceOptionsAdapter().Adapt(resource);

        CollectionAssert.AreEqual(new[] { 42 }, options.EventIds);
        CollectionAssert.AreEqual(new[] { "notepad.exe" }, options.ProcessNames);
    }
}
