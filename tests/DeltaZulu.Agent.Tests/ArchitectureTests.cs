using System.Reflection;
using DeltaZulu.Agent.Shared.Pipeline;
using DeltaZulu.Agent.Shared.Pipeline.Events;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ArchitectureTests
{
    [TestMethod]
    public void SharedAssembly_HasNoProjectReferences()
    {
        var sharedAssembly = typeof(SourceEvent).Assembly;
        var projectRefs = GetProjectReferences(sharedAssembly);

        Assert.IsEmpty(projectRefs,
            $"Shared must have zero DeltaZulu project references but found: {string.Join(", ", projectRefs)}");
    }

    [TestMethod]
    public void SharedAssembly_DoesNotReferenceLegacyProjects()
    {
        var sharedAssembly = typeof(ResourcePipeline).Assembly;
        var projectRefs = GetProjectReferences(sharedAssembly);

        Assert.IsEmpty(projectRefs,
            $"Shared should not reference other DeltaZulu projects but found: {string.Join(", ", projectRefs)}");
    }

    [TestMethod]
    public void InfrastructureProjects_DoNotReferenceOtherInfrastructure()
    {
        var infraAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DeltaZulu.Agent.Inputs",
            "DeltaZulu.Agent.Kql",
            "DeltaZulu.Agent.Shared",
            "DeltaZulu.Agent.Outputs"
        };

        var allowedProjectRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DeltaZulu.Agent.Shared",
            "DeltaZulu.DurableBuffer",
            "DeltaZulu.Relp"
        };

        foreach (var assemblyName in infraAssemblyNames)
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.Load(assemblyName);
            }
            catch (FileNotFoundException)
            {
                continue;
            }

            var projectRefs = GetProjectReferences(assembly);
            var illegalRefs = projectRefs
                .Where(name => !allowedProjectRefs.Contains(name))
                .ToList();

            Assert.IsEmpty(illegalRefs,
                $"{assemblyName} has disallowed project references: {string.Join(", ", illegalRefs)}");
        }
    }

    private static List<string> GetProjectReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Where(name => name.Name!.StartsWith("DeltaZulu", StringComparison.Ordinal) ||
                           name.Name!.Equals("Relp", StringComparison.Ordinal))
            .Select(name => name.Name!)
            .ToList();
}
