using System.Reflection;
using DeltaZulu.Pipeline.Core;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ArchitectureTests
{
    [TestMethod]
    public void PipelineAssembly_HasNoProjectReferences()
    {
        var pipelineAssembly = typeof(SourceEvent).Assembly;
        var projectRefs = GetProjectReferences(pipelineAssembly);

        Assert.IsEmpty(projectRefs,
            $"Pipeline must have zero DeltaZulu project references but found: {string.Join(", ", projectRefs)}");
    }

    [TestMethod]
    public void PipelineAssembly_DoesNotReferenceLegacyProjects()
    {
        var pipelineAssembly = typeof(ResourcePipeline).Assembly;
        var projectRefs = GetProjectReferences(pipelineAssembly);

        Assert.IsEmpty(projectRefs,
            $"Pipeline should not reference other DeltaZulu projects but found: {string.Join(", ", projectRefs)}");
    }

    [TestMethod]
    public void InfrastructureProjects_DoNotReferenceOtherInfrastructure()
    {
        var infraAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DeltaZulu.Agent.Inputs",
            "DeltaZulu.Agent.Kql",
            "DeltaZulu.Agent.Pipeline",
            "DeltaZulu.Agent.Outputs"
        };

        var allowedProjectRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DeltaZulu.Agent.Pipeline",
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
