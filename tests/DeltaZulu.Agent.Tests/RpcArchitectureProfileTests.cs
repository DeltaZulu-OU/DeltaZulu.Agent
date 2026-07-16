namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class RpcArchitectureProfileTests
{
    [TestMethod]
    public void RpcP0Profile_TargetsP0InterfacesNormalizesBracedUuidsAndPreservesRawReplayablePayloads()
    {
        var profile = File.ReadAllText(ProfilePath("windows", "etw", "rpc-p0.yaml"));

        Assert.Contains("id: windows.etw.rpc.p0", profile);
        Assert.Contains("preserveRawEvent: true", profile);
        Assert.Contains("metadataEnvelope: true", profile);
        Assert.Contains("InterfaceUuidNormalized = trim", profile);
        Assert.IsFalse(profile.Contains("isempty(InterfaceUuidNormalized)", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("367abb81-9844-35f1-ad32-98f038001003", profile);
        Assert.Contains("e3514235-4b06-11d1-ab04-00c04fc2dcd2", profile);
        Assert.Contains("svcctl", profile);
        Assert.Contains("drsuapi", profile);
    }

    [TestMethod]
    public void BaselineSecurityProfile_ContinuesSuppressingGlobal5156Volume()
    {
        var profile = File.ReadAllText(ProfilePath("windows", "eventlog", "security.yaml"));

        Assert.Contains("5152, 5156, 5157", profile);
    }

    [TestMethod]
    public void RpcCorrelationSecurityProfile_RetainsOnlyCorrelationEventsAndGates5156()
    {
        var profile = File.ReadAllText(ProfilePath("windows", "eventlog", "security-rpc-correlation.yaml"));

        Assert.Contains("id: windows.eventlog.security.rpc-correlation", profile);
        Assert.Contains("| where EventId in (4624, 4662, 5156)", profile);
        Assert.Contains(@"C:\Windows\System32\services.exe", profile);
        Assert.Contains(@"C:\Windows\System32\lsass.exe", profile);
        Assert.Contains("ApplicationPath in~", profile);
        Assert.Contains("ApplicationName in~", profile);
        Assert.Contains("Application_Name in~", profile);
        Assert.Contains("['Application Name'] in~", profile);
        Assert.Contains("DestPort between (49152 .. 65535)", profile);
        Assert.Contains("DestinationPort between (49152 .. 65535)", profile);
        Assert.Contains("Destination_Port between (49152 .. 65535)", profile);
        Assert.Contains("['Destination Port'] between (49152 .. 65535)", profile);
    }


    [TestMethod]
    public void Repository_DoesNotReferenceDeletedGenericRpcProfile()
    {
        var root = RepoRoot();
        var forbidden = new[] { "profiles/windows/etw/rpc.yaml" };
        var searchRoots = new[] { "config", "profiles", "src", "tests", "README.md", "Directory.Build.props" };
        foreach (var relativeRoot in searchRoots)
        {
            var path = Path.Combine(root, relativeRoot);
            var files = File.Exists(path)
                ? new[] { path }
                : Directory.Exists(path)
                    ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    : Array.Empty<string>();

            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                if (relative is "profiles/windows/etw/rpc-p0.yaml" or "tests/DeltaZulu.Agent.Tests/RpcArchitectureProfileTests.cs")
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                foreach (var value in forbidden)
                {
                    Assert.IsFalse(text.Contains(value, StringComparison.OrdinalIgnoreCase), $"Found stale reference '{value}' in {relative}.");
                }
            }
        }
    }

    private static string ProfilePath(params string[] segments)
    {
        var candidate = Path.Combine(new[] { RepoRoot(), "profiles" }.Concat(segments).ToArray());
        if (File.Exists(candidate))
        {
            return candidate;
        }

        Assert.Fail($"Could not locate profile path: {Path.Combine(segments)}");
        return string.Empty;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) &&
                Directory.Exists(Path.Combine(directory.FullName, "profiles")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("Could not locate repository root.");
        return string.Empty;
    }
}
