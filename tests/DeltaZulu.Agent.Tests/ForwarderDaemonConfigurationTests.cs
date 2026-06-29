using DeltaZulu.Agent.Daemon;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ForwarderDaemonConfigurationTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(101)]
    public void Validate_RejectsResourceQuotaCpuPercentOutsideSupportedRange(int cpuPercent)
    {
        var configuration = new ForwarderDaemonConfiguration {
            ResourceQuotas = new ForwarderDaemonResourceQuotaConfiguration {
                CpuPercent = cpuPercent
            }
        };

        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            YamlForwarderDaemonConfigurationLoader.Validate(configuration, "dzagentd.yaml"));
        Assert.Contains("resourceQuotas.cpuPercent must be between 1 and 100", exception.Message);
    }

    [TestMethod]
    public void YamlForwarderDaemonConfigurationLoader_LoadFile_LoadsResourceQuotaCpuPercent()
    {
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.Path, "dzagentd.yaml");
        File.WriteAllText(configPath, """
            id: test-agent
            profilesPath: profiles
            pipeline:
              input:
                mode: profiles
              filter:
                mode: profiles
              output:
                mode: console
            buffer:
              path: ./buffer
            relp:
              endpoints:
                - host: 127.0.0.1
                  port: 6514
            resourceQuotas:
              cpuPercent: 20
            """);

        var configuration = new YamlForwarderDaemonConfigurationLoader().LoadFile(configPath);

        Assert.AreEqual(20, configuration.ResourceQuotas.CpuPercent);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dza-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}