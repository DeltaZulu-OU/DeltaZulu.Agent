using DeltaZulu.Pipeline.Core.Planning;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ExecutionPlanCompilerTests
{
    [TestMethod]
    public void Compile_GroupsProfilesWithSamePhysicalResource()
    {
        var profiles = new[] {
            CreateProfile("b", "syslog-tcp", port: 514),
            CreateProfile("a", "syslog-tcp", port: 514)
        };

        var plan = new ExecutionPlanCompiler().Compile(profiles);

        Assert.HasCount(1, plan.Acquisitions);
        var acquisition = plan.Acquisitions[0];
        Assert.AreEqual("syslog-tcp", acquisition.Kind);
        Assert.AreEqual("rfc6587-or-newline", acquisition.Framing);
        Assert.AreEqual("text", acquisition.PayloadFormat);
        Assert.AreEqual("syslog-pri", acquisition.AdmissionPolicy);
        Assert.AreEqual("syslog-tcp", acquisition.ParserDomain);
        Assert.AreSequenceEqual(new[] { "a", "b" }, acquisition.ProfileIds.ToArray());
    }

    [TestMethod]
    public void Compile_RejectsConflictingSettingsForSamePhysicalResource()
    {
        var profiles = new[] {
            CreateProfile("a", "syslog-tcp", port: 514, parserDomain: "auth"),
            CreateProfile("b", "syslog-tcp", port: 514, parserDomain: "network")
        };

        var exception = Assert.ThrowsExactly<ExecutionPlanCompilationException>(() =>
            new ExecutionPlanCompiler().Compile(profiles));

        Assert.Contains("conflicting acquisition settings", exception.Message);
    }

    [TestMethod]
    public void Compile_IgnoresDisabledProfiles()
    {
        var profiles = new[] {
            CreateProfile("enabled", "file", path: "/var/log/auth.log"),
            CreateProfile("disabled", "file", path: "/var/log/other.log", enabled: false)
        };

        var plan = new ExecutionPlanCompiler().Compile(profiles);

        Assert.HasCount(1, plan.Acquisitions);
        Assert.AreSequenceEqual(new[] { "enabled" }, plan.Acquisitions[0].ProfileIds.ToArray());
    }

    private static ResourceProfile CreateProfile(
        string id,
        string family,
        int? port = null,
        string? path = null,
        string? parserDomain = null,
        bool enabled = true)
    {
        var options = new Dictionary<string, object?>();
        if (port is not null)
        {
            options["port"] = port.Value;
        }

        if (path is not null)
        {
            options["path"] = path;
        }

        if (parserDomain is not null)
        {
            options["parserDomain"] = parserDomain;
        }

        return new ResourceProfile {
            Id = id,
            Enabled = enabled,
            Resource = new ResourceDescriptor {
                Family = family,
                Platform = "linux",
                Options = options
            }
        };
    }
}
