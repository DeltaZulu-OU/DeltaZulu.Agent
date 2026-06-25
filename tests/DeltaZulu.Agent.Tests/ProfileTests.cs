using DeltaZulu.Agent.Profiles;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ProfileTests
{
    [TestMethod]
    public void Validate_AcceptsMinimalSupportedProfile()
    {
        var errors = new ResourceProfileValidator().Validate(CreateValidProfile());

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void Validate_AcceptsProfileWithoutFilterQuery()
    {
        var profile = CreateValidProfile();
        profile.Filter = new ResourceFilter();

        var errors = new ResourceProfileValidator().Validate(profile);

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void Validate_AcceptsWmiCondition()
    {
        var profile = CreateValidProfile();
        profile.Condition = new ResourceCondition
        {
            Type = "wmi",
            Query = "select * from Win32_OperatingSystem where ProductType=2"
        };

        var errors = new ResourceProfileValidator().Validate(profile);

        Assert.AreEqual(0, errors.Count);
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

        StringAssert.Contains(exception.Message, "Invalid resource profile 'profile.yaml'");
        StringAssert.Contains(exception.Message, "id is required.");
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

            Assert.AreEqual(0, result.Errors.Count);
            Assert.AreEqual(1, result.Warnings.Count);
            Assert.AreEqual(0, result.Profiles.Count);
            StringAssert.Contains(result.Warnings[0], "optional.invalid");
            StringAssert.Contains(result.Warnings[0], "Only output.format: ndjson is supported");
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

            Assert.AreEqual(1, result.Errors.Count);
            Assert.AreEqual(0, result.Warnings.Count);
            Assert.AreEqual(0, result.Profiles.Count);
            StringAssert.Contains(result.Errors[0], "mandatory.invalid");
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    private static ResourceProfile CreateValidProfile() => new()
    {
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
