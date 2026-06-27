using System.Reflection;
using DeltaZulu.Agent.Application.Pipelines;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ArchitectureTests
{
    [TestMethod]
    public void DomainAssembly_HasNoProjectReferences()
    {
        var domainAssembly = typeof(DeltaZulu.Agent.Core.Events.SourceEvent).Assembly;
        var projectRefs = GetProjectReferences(domainAssembly);

        Assert.IsEmpty(projectRefs,
            $"Domain must have zero project references but found: {string.Join(", ", projectRefs)}");
    }

    [TestMethod]
    public void ApplicationAssembly_ReferencesOnlyDomain()
    {
        var appAssembly = typeof(ResourcePipeline).Assembly;
        var projectRefs = GetProjectReferences(appAssembly);

        Assert.HasCount(1, projectRefs,
            $"Application should reference only Domain but found: {string.Join(", ", projectRefs)}");
        Assert.AreEqual("DeltaZulu.Agent.Domain", projectRefs[0]);
    }

    [TestMethod]
    public void InfrastructureProjects_DoNotReferenceOtherInfrastructure()
    {
        var infraAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DeltaZulu.Agent.Inputs.Syslog",
            "DeltaZulu.Agent.Inputs.Files",
            "DeltaZulu.Agent.Inputs.Relp",
            "DeltaZulu.Agent.Inputs.Auditd",
            "DeltaZulu.Agent.Inputs.Windows",
            "DeltaZulu.Agent.Kql",
            "DeltaZulu.Agent.Profiles",
            "DeltaZulu.Agent.Outputs.Ndjson",
            "DeltaZulu.Agent.Forwarder"
        };

        var allowedProjectRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DeltaZulu.Agent.Domain",
            "DeltaZulu.Agent.Application",
            "DeltaZulu.DurableBuffer",
            "Relp"
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
