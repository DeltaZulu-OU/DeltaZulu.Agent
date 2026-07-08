namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class RpcArchitectureProfileTests
{
    [TestMethod]
    public void RpcP0Profile_TargetsP0InterfacesNormalizesBracedUuidsAndPreservesRawReplayablePayloads()
    {
        var profile = File.ReadAllText(ProfilePath("windows", "etw", "rpc-p0.yaml"));

        StringAssert.Contains(profile, "id: windows.etw.rpc.p0");
        StringAssert.Contains(profile, "preserveRawEvent: true");
        StringAssert.Contains(profile, "metadataEnvelope: true");
        StringAssert.Contains(profile, "InterfaceUuidNormalized = trim");
        Assert.IsFalse(profile.Contains("isempty(InterfaceUuidNormalized)", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(profile, "367abb81-9844-35f1-ad32-98f038001003");
        StringAssert.Contains(profile, "e3514235-4b06-11d1-ab04-00c04fc2dcd2");
        StringAssert.Contains(profile, "svcctl");
        StringAssert.Contains(profile, "drsuapi");
    }

    [TestMethod]
    public void BaselineSecurityProfile_ContinuesSuppressingGlobal5156Volume()
    {
        var profile = File.ReadAllText(ProfilePath("windows", "eventlog", "security.yaml"));

        StringAssert.Contains(profile, "5152, 5156, 5157");
    }

    [TestMethod]
    public void RpcCorrelationSecurityProfile_RetainsOnlyCorrelationEventsAndGates5156()
    {
        var profile = File.ReadAllText(ProfilePath("windows", "eventlog", "security-rpc-correlation.yaml"));

        StringAssert.Contains(profile, "id: windows.eventlog.security.rpc-correlation");
        StringAssert.Contains(profile, "| where EventId in (4624, 4662, 5156)");
        StringAssert.Contains(profile, @"C:\Windows\System32\services.exe");
        StringAssert.Contains(profile, @"C:\Windows\System32\lsass.exe");
        StringAssert.Contains(profile, "ApplicationPath in~");
        StringAssert.Contains(profile, "ApplicationName in~");
        StringAssert.Contains(profile, "Application_Name in~");
        StringAssert.Contains(profile, "['Application Name'] in~");
        StringAssert.Contains(profile, "DestPort between (49152 .. 65535)");
        StringAssert.Contains(profile, "DestinationPort between (49152 .. 65535)");
        StringAssert.Contains(profile, "Destination_Port between (49152 .. 65535)");
        StringAssert.Contains(profile, "['Destination Port'] between (49152 .. 65535)");
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
