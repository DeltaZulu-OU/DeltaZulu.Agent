using System.Reflection;
using DeltaZulu.Pipeline.Core;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ArchitectureTests
{
    [TestMethod]
    public void PipelineAssembly_ReferencesOnlyExternalPipelineDependencies()
    {
        var pipelineAssembly = typeof(SourceEvent).Assembly;
        var projectRefs = GetProjectReferences(pipelineAssembly);

        var unexpectedRefs = projectRefs
            .Where(name => name is not "DeltaZulu.DurableBuffer" and not "DeltaZulu.Relp" and not "Relp")
            .ToList();

        Assert.IsEmpty(unexpectedRefs,
            $"Pipeline must only reference external pipeline dependencies but found: {string.Join(", ", unexpectedRefs)}");
    }

    [TestMethod]
    public void PipelineAssembly_DoesNotReferenceAgentProjects()
    {
        var pipelineAssembly = typeof(ResourcePipeline).Assembly;
        var agentRefs = GetProjectReferences(pipelineAssembly)
            .Where(name => name.StartsWith("DeltaZulu.Agent", StringComparison.Ordinal))
            .ToList();

        Assert.IsEmpty(agentRefs,
            $"Pipeline must remain generic and must not reference agent projects: {string.Join(", ", agentRefs)}");
    }

    private static List<string> GetProjectReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Where(name => name.Name!.StartsWith("DeltaZulu", StringComparison.Ordinal) ||
                           name.Name!.Equals("Relp", StringComparison.Ordinal))
            .Select(name => name.Name!)
            .ToList();
}
