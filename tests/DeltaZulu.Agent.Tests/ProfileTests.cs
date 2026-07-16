using DeltaZulu.Pipeline.Core.Profiles;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Agent.Filter.Kql;
using System.Reactive.Linq;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ProfileTests
{
    [TestMethod]
    public void Validate_AcceptsMinimalSupportedProfile()
    {
        var errors = new ResourceProfileValidator().Validate(CreateValidProfile());

        Assert.IsEmpty(errors);
    }

    [TestMethod]
    public void Validate_AcceptsProfileWithoutFilterQuery()
    {
        var profile = CreateValidProfile();
        profile.Filter = new ResourceFilter();

        var errors = new ResourceProfileValidator().Validate(profile);

        Assert.IsEmpty(errors);
    }

    [TestMethod]
    public void Validate_AcceptsWmiCondition()
    {
        var profile = CreateValidProfile();
        profile.Condition = new ResourceCondition {
            Type = "wmi",
            Query = "select * from Win32_OperatingSystem where ProductType=2",
            Mandatory = false
        };

        var errors = new ResourceProfileValidator().Validate(profile);

        Assert.IsEmpty(errors);
        Assert.IsFalse(profile.Condition.Mandatory);
    }

    [TestMethod]
    public void Validate_RejectsIncompleteCondition()
    {
        var profile = CreateValidProfile();
        profile.Condition = new ResourceCondition { Type = "eventlog" };

        var errors = new ResourceProfileValidator().Validate(profile);

        Assert.HasCount(1, errors);
        Assert.Contains("condition.query is required when condition is specified.", errors.ToList());
    }

    [TestMethod]
    public void Validate_AcceptsAnyConditionTypeWithQuery()
    {
        // Core validates condition shape only; whether a condition.type has a registered
        // evaluator on this host/platform is decided by DeltaZulu.Agent.Filter at runtime.
        var profile = CreateValidProfile();
        profile.Condition = new ResourceCondition { Type = "systemd-unit", Query = "sshd.service" };

        var errors = new ResourceProfileValidator().Validate(profile);

        Assert.IsEmpty(errors);
    }

    [TestMethod]
    public void Validate_ReportsBusinessRuleViolationsTogether()
    {
        var profile = CreateValidProfile();
        profile.SchemaVersion = 0;
        profile.Filter.Language = "sql";
        profile.Output.Format = "json";
        profile.Output.PreserveOriginalFieldNames = false;

        var errors = new ResourceProfileValidator().Validate(profile);

        Assert.Contains("schemaVersion must be greater than zero.", errors.ToList());
        Assert.Contains("Only filter.language: kql is supported in this implementation.", errors.ToList());
        Assert.Contains("Only output.format: ndjson is supported in this implementation.", errors.ToList());
        Assert.Contains("preserveOriginalFieldNames must remain true. Server-side normalization owns semantic field mapping.", errors.ToList());
    }

    [TestMethod]
    public void ThrowIfInvalid_IncludesSourceNameInException()
    {
        var profile = CreateValidProfile();
        profile.Id = string.Empty;

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => new ResourceProfileValidator().ThrowIfInvalid(profile, "profile.yaml"));

        Assert.Contains("Invalid resource profile 'profile.yaml'", exception.Message);
        Assert.Contains("id is required.", exception.Message);
    }

    [TestMethod]
    public void LoadFile_LoadsFamilySpecificResourceOptionsAsOpaqueBag()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(path, """
schemaVersion: 1
id: windows.etw.test
name: Windows ETW test
version: 1.0.0
enabled: true
resource:
  platform: windows
  family: etw
  mode: managed
  session: DeltaZulu-Test
  provider: Microsoft-Windows-Test
  options:
    eventIds: [1, 2]
    excludedEventIds: [3]
    captureStacks: true
    stackEventIds: [4]
    excludedStackEventIds: [5]
    processIds: [1234]
    processNames: [notepad.exe]
    payloadFields: [Image, CommandLine]
    enableInContainers: true
    enableSourceContainerTracking: true
input:
  table: Etw
  schema: WindowsEtw.Native
output:
  format: ndjson
  preserveOriginalFieldNames: true
filter:
  language: kql
  query: Etw | take 1
""");

            var profile = new YamlResourceProfileLoader().LoadFile(path);

            // Core only knows about the opaque Options bag; ETW-specific interpretation is the
            // job of DeltaZulu.Pipeline.Inputs.Etw.EtwResourceOptionsAdapter (see its own tests).
            CollectionAssert.AreEqual(new[] { 1, 2 }, profile.Resource.Options.GetIntList("eventIds"));
            CollectionAssert.AreEqual(new[] { 3 }, profile.Resource.Options.GetIntList("excludedEventIds"));
            Assert.IsTrue(profile.Resource.Options.GetBool("captureStacks"));
            CollectionAssert.AreEqual(new[] { 4 }, profile.Resource.Options.GetIntList("stackEventIds"));
            CollectionAssert.AreEqual(new[] { 5 }, profile.Resource.Options.GetIntList("excludedStackEventIds"));
            CollectionAssert.AreEqual(new[] { 1234 }, profile.Resource.Options.GetIntList("processIds"));
            CollectionAssert.AreEqual(new[] { "notepad.exe" }, profile.Resource.Options.GetStringList("processNames"));
            CollectionAssert.AreEqual(new[] { "Image", "CommandLine" }, profile.Resource.Options.GetStringList("payloadFields"));
            Assert.IsTrue(profile.Resource.Options.GetBool("enableInContainers"));
            Assert.IsTrue(profile.Resource.Options.GetBool("enableSourceContainerTracking"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadFile_AppliesEtwTableAndSchemaDefaultsWhenInputIsOmitted()
    {
        var profile = new YamlResourceProfileLoader().LoadFile(Path.Combine("profiles", "windows", "etw", "tcpip.yaml"));

        Assert.AreEqual("Etw", profile.Input.Table);
        Assert.AreEqual("WindowsEtw.Native", profile.Input.Schema);
        Assert.IsTrue(profile.Filter.Query.TrimStart().StartsWith(profile.Input.Table, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void LoadFile_SecurityProfileDoesNotProjectFieldsForRawLogPreservation()
    {
        var profile = new YamlResourceProfileLoader().LoadFile(Path.Combine("profiles", "windows", "eventlog", "security.yaml"));

        Assert.IsFalse(profile.Resource.Options.ContainsKey("sidObservation"));
        Assert.IsFalse(profile.Resource.Options.ContainsKey("processObservation"));
        Assert.IsFalse(profile.Filter.Query.Contains("| project", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(profile.Filter.Query.Contains("_resolved", StringComparison.OrdinalIgnoreCase));
    }


    [TestMethod]
    public void LoadFile_AuditdProfileKeepsAllAuditdEvents()
    {
        var profile = new YamlResourceProfileLoader().LoadFile(Path.Combine("profiles", "linux", "auditd", "auditd.yaml"));

        Assert.AreEqual("linux.auditd", profile.Id);
        Assert.IsEmpty(profile.Resource.RecordTypes);
        Assert.IsFalse(profile.Filter.Query.Contains("execve", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(profile.Filter.Query.Contains("source =~ \"auditd\"", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void LoadDirectory_WindowsProfilesDoNotProjectFieldsForRawLogPreservation()
    {
        foreach (var profile in new YamlResourceProfileLoader().LoadDirectory(Path.Combine("profiles", "windows")).Profiles)
        {
            Assert.IsFalse(profile.Filter.Query.Contains("| project", StringComparison.OrdinalIgnoreCase), $"Profile {profile.Id} should preserve raw log fields instead of projecting a fixed field list.");
        }
    }

    [TestMethod]
    public void LoadDirectory_WarnsAndSkipsInvalidOptionalProfile()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "optional.yaml"), """
schemaVersion: 1
id: optional.invalid
name: Optional Invalid
version: 1.0.0
enabled: true
mandatory: false
resource:
  platform: linux
  family: syslog
input:
  table: Source
  schema: RawMessage:string
output:
  format: json
  preserveOriginalFieldNames: true
filter:
  language: kql
  query: Source | take 1
""");

            var result = new YamlResourceProfileLoader().LoadDirectory(directory.FullName);

            Assert.IsEmpty(result.Errors);
            Assert.HasCount(1, result.Warnings);
            Assert.IsEmpty(result.Profiles);
            Assert.Contains("optional.invalid", result.Warnings[0]);
            Assert.Contains("Only output.format: ndjson is supported", result.Warnings[0]);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [TestMethod]
    public void LoadDirectory_ErrorsOnInvalidMandatoryProfile()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "mandatory.yaml"), """
schemaVersion: 1
id: mandatory.invalid
name: Mandatory Invalid
version: 1.0.0
enabled: true
mandatory: true
resource:
  platform: linux
  family: syslog
input:
  table: Source
  schema: RawMessage:string
output:
  format: json
  preserveOriginalFieldNames: true
filter:
  language: kql
  query: Source | take 1
""");

            var result = new YamlResourceProfileLoader().LoadDirectory(directory.FullName);

            Assert.HasCount(1, result.Errors);
            Assert.IsEmpty(result.Warnings);
            Assert.IsEmpty(result.Profiles);
            Assert.Contains("mandatory.invalid", result.Errors[0]);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [TestMethod]
    public void LoadDirectory_LoadsBuiltInWindowsEventLogProfiles()
    {
        var directory = FindRepositoryPath("profiles", "windows", "eventlog");

        var result = new YamlResourceProfileLoader().LoadDirectory(directory);

        Assert.IsEmpty(result.Errors);
        Assert.IsEmpty(result.Warnings);
        Assert.IsGreaterThanOrEqualTo(70, result.Profiles.Count);
        Assert.Contains("windows.eventlog.security", result.Profiles.Select(profile => profile.Id).ToList());
        Assert.Contains("windows.eventlog.system-service-control-manager", result.Profiles.Select(profile => profile.Id).ToList());
        Assert.Contains("windows.eventlog.windowsupdateclient-operational", result.Profiles.Select(profile => profile.Id).ToList());
    }

    [TestMethod]
    public void CheckedInProfiles_ParseWithThePinnedRxKqlEngine()
    {
        var directory = FindRepositoryPath("profiles");
        var result = new YamlResourceProfileLoader().LoadDirectory(directory);
        Assert.IsEmpty(result.Errors, $"Profile loading errors: {string.Join("; ", result.Errors)}");

        var failures = new List<string>();
        foreach (var profile in result.Profiles)
        {
            // A disabled profile is still checked-in authoring content. Enable it
            // only for this parse/startup contract; no source rows are supplied.
            profile.Enabled = true;
            using var executor = new ResourceKqlProfileExecutor();
            try
            {
                executor.Execute(Observable.Empty<SourceEvent>(), profile).ToList().Wait();
            }
            catch (Exception ex)
            {
                failures.Add($"{profile.Id}: {ex.GetBaseException().Message}");
            }
        }

        Assert.IsEmpty(failures, $"Checked-in profiles rejected by Microsoft.Rx.Kql 3.5.3: {string.Join("; ", failures)}");
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository path '{Path.Combine(segments)}'.");
    }


    private static ResourceProfile CreateValidProfile() => new() {
        SchemaVersion = 1,
        Id = "linux.syslog.sshd",
        Name = "SSH logins",
        Version = "1.0.0",
        Resource = new ResourceDescriptor { Platform = "linux", Family = "syslog" },
        Input = new ResourceInputContract { Table = "Source", Schema = "RawMessage:string" },
        Filter = new ResourceFilter { Language = "kql", Query = "Source | take 1" },
        Output = new ResourceOutputContract { Format = "ndjson", PreserveOriginalFieldNames = true }
    };
}
