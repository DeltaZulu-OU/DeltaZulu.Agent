using DeltaZulu.Pipeline.Core.Profiles;

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

        CollectionAssert.Contains(errors.ToList(), "Only condition.type: wmi is supported in this implementation.");
        CollectionAssert.Contains(errors.ToList(), "condition.query is required when condition is specified.");
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

        CollectionAssert.Contains(errors.ToList(), "schemaVersion must be greater than zero.");
        CollectionAssert.Contains(errors.ToList(), "Only filter.language: kql is supported in this implementation.");
        CollectionAssert.Contains(errors.ToList(), "Only output.format: ndjson is supported in this implementation.");
        CollectionAssert.Contains(errors.ToList(), "preserveOriginalFieldNames must remain true. Server-side normalization owns semantic field mapping.");
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
    public void LoadFile_LoadsManagedEtwProviderEnablementOptions()
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
  etwEventIds: [1, 2]
  etwExcludedEventIds: [3]
  etwCaptureStacks: true
  etwStackEventIds: [4]
  etwExcludedStackEventIds: [5]
  etwProcessIds: [1234]
  etwProcessNames: [notepad.exe]
  etwEnableInContainers: true
  etwEnableSourceContainerTracking: true
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

            CollectionAssert.AreEqual(new[] { 1, 2 }, profile.Resource.EtwEventIds);
            CollectionAssert.AreEqual(new[] { 3 }, profile.Resource.EtwExcludedEventIds);
            Assert.IsTrue(profile.Resource.EtwCaptureStacks);
            CollectionAssert.AreEqual(new[] { 4 }, profile.Resource.EtwStackEventIds);
            CollectionAssert.AreEqual(new[] { 5 }, profile.Resource.EtwExcludedStackEventIds);
            CollectionAssert.AreEqual(new[] { 1234 }, profile.Resource.EtwProcessIds);
            CollectionAssert.AreEqual(new[] { "notepad.exe" }, profile.Resource.EtwProcessNames);
            Assert.IsTrue(profile.Resource.EtwEnableInContainers);
            Assert.IsTrue(profile.Resource.EtwEnableSourceContainerTracking);
        }
        finally
        {
            File.Delete(path);
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