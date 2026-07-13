using DeltaZulu.Agent.SchemaMetadata;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Profiles;
using DeltaZulu.Pipeline.Core.Windows;
using DeltaZulu.Pipeline.Inputs.Auditd;
using DeltaZulu.Pipeline.Inputs.Files;
using DeltaZulu.Pipeline.Inputs.Syslog;

#if NET10_0_WINDOWS
using DeltaZulu.Pipeline.Inputs.Windows;
#endif

namespace DeltaZulu.Agent.ProfileWorkbench;

public sealed class WorkbenchSourceRegistry
{
    private readonly Action<string>? _warn;

    public WorkbenchSourceRegistry(Action<string>? warn = null)
    {
        _warn = warn;
    }

    public WorkbenchSourceCandidate CandidateFromProfile(ResourceProfile profile)
    {
        EnsureProfileEnabled(profile);
        var sourceKind = NormalizeFamily(profile.Resource.Family);
        var table = string.IsNullOrWhiteSpace(profile.Input.Table) ? "Source" : profile.Input.Table;
        var schema = SchemaTextParser.Parse(table, profile.Input.Schema, sourceKind, executable: false);
        var display = BuildDisplayName(profile);
        var requiresBinding = RequiresExternalBinding(sourceKind, profile);
        var resource = FirstNonWhiteSpace(profile.Resource.Service, profile.Resource.Channel, profile.Resource.Session, profile.Resource.Provider);

        return new WorkbenchSourceCandidate(sourceKind, display, table, resource, schema, requiresBinding);
    }

    public BoundWorkbenchSource Bind(ResourceProfile profile, string? pathOrResource, WorkbenchRunMode mode)
    {
        EnsureProfileEnabled(profile);
        var sourceKind = NormalizeFamily(profile.Resource.Family);
        var table = string.IsNullOrWhiteSpace(profile.Input.Table) ? "Source" : profile.Input.Table;
        var schema = SchemaTextParser.Parse(table, profile.Input.Schema, sourceKind, executable: true);
        var bindingOverride = NormalizeBindingOverride(pathOrResource);
        var input = CreateInput(profile, sourceKind, bindingOverride, mode);

        return new BoundWorkbenchSource(sourceKind, BuildDisplayName(profile), table, schema, input);
    }

    public BoundWorkbenchSource BindTail(string query, string path, string? inputKind, int limit)
    {
        var sourceKind = NormalizeFamily(inputKind ?? DetectFileKind(path));
        var table = DefaultTable(sourceKind);
        var schema = sourceKind == "lines"
            ? SchemaTextParser.Lines(table)
            : SchemaTextParser.Parse(table, DefaultSchema(sourceKind), sourceKind, "ParserContract", executable: true);
        var profile = new ResourceProfile {
            SchemaVersion = 1,
            Id = $"cli.tail.{sourceKind}",
            Name = $"Tail {sourceKind}",
            Version = "1.0.0",
            Resource = new ResourceDescriptor { Platform = OperatingSystem.IsWindows() ? "windows" : "local", Family = sourceKind },
            Input = new ResourceInputContract { Table = table, Schema = DefaultSchema(sourceKind) },
            Filter = new ResourceFilter { Language = "kql", Query = query },
            Output = new ResourceOutputContract { Format = "table", PreserveOriginalFieldNames = true }
        };

        var input = CreateInput(profile, sourceKind, path, WorkbenchRunMode.Follow);
        return new BoundWorkbenchSource(sourceKind, path, table, schema, input);
    }

    private static void EnsureProfileEnabled(ResourceProfile profile)
    {
        if (!profile.Enabled)
        {
            throw new InvalidOperationException($"profile '{profile.Id}' is disabled and cannot be used by the workbench.");
        }
    }
    private ISourceInput CreateInput(ResourceProfile profile, string sourceKind, string? pathOrResource, WorkbenchRunMode mode)
    {
        var bindingOverride = NormalizeBindingOverride(pathOrResource);

        return sourceKind switch {
            "syslog" => new SyslogFileTailInput(RequirePath(bindingOverride, "syslog")),
            "auditd" => mode == WorkbenchRunMode.Follow
                            ? new AuditdTailSourceInput(RequirePath(pathOrResource, "auditd"))
                            : new AuditdFileInput(RequirePath(pathOrResource, "auditd")),
            "csv" => new CsvFileInput(RequirePath(bindingOverride, "csv")),
            "lines" => new LinesSourceInput(RequirePath(bindingOverride, "lines"), mode == WorkbenchRunMode.Follow),
#if NET10_0_WINDOWS
            "eventlog" => new WindowsEventLogInput(
                FirstNonWhiteSpace(bindingOverride, profile.Resource.Channel) ?? throw new ArgumentException($"profile '{profile.Id}' eventlog binding requires resource.channel or an explicit channel."),
                startPosition: EventLogStartPosition.Lookback,
                lookback: TimeSpan.FromMinutes(5)),
            "evtx" => new EvtxFileInput(RequirePath(bindingOverride, "evtx")),
            "etl" => new EtlFileInput(RequirePath(bindingOverride, "etl"), warn: _warn),
            "etw" => CreateEtwInput(profile, bindingOverride),
#else
            "eventlog" or "evtx" or "etl" or "etw" => throw new PlatformNotSupportedException($"{sourceKind} is available from the windows build."),
#endif
            _ => new LinesSourceInput(RequirePath(bindingOverride, "lines"), mode == WorkbenchRunMode.Follow)
        };
    }

#if NET10_0_WINDOWS
    private ISourceInput CreateEtwInput(ResourceProfile profile, string? sessionOverride)
    {
        var session = FirstNonWhiteSpace(sessionOverride, profile.Resource.Session) ?? throw new ArgumentException($"profile '{profile.Id}' etw binding requires resource.session or an explicit session name.");
        if (profile.Resource.Mode.Equals("managed", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(profile.Resource.Provider))
            {
                throw new ArgumentException($"profile '{profile.Id}' managed etw binding requires resource.provider.");
            }

            return new ManagedEtwSessionInput(session, profile.Resource.Provider, profile.Resource, warn: _warn);
        }

        return new EtwSessionInput(session, profile.Resource, warn: _warn);
    }
#endif
    private static string RequirePath(string? path, string sourceKind)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"{sourceKind} workbench binding requires a local path or resource.");
        }

        return path;
    }

    private static string? NormalizeBindingOverride(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string NormalizeFamily(string? family) => family?.Trim().ToLowerInvariant() switch {
        "syslog" => "syslog",
        "auditd" => "auditd",
        "csv" => "csv",
        "line" or "lines" => "lines",
        "eventlog" or "windows.eventlog" => "eventlog",
        "evtx" => "evtx",
        "etl" => "etl",
        "etw" => "etw",
        _ => "lines"
    };

    private static string DetectFileKind(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".csv")
        {
            return "csv";
        }

        var fileName = Path.GetFileName(path).ToLowerInvariant();
        if (fileName.Contains("audit", StringComparison.OrdinalIgnoreCase))
        {
            return "auditd";
        }

        return fileName is "syslog" or "messages" or "secure"
            || fileName.EndsWith(".syslog", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("auth.log", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("kern.log", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("daemon.log", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("mail.log", StringComparison.OrdinalIgnoreCase)
            ? "syslog"
            : "lines";
    }

    private static string DefaultTable(string sourceKind) => sourceKind switch {
        "syslog" => "Syslog",
        "auditd" => "Auditd",
        "csv" => "Csv",
        "lines" => "Lines",
        _ => "Source"
    };

    private static string DefaultSchema(string sourceKind) => sourceKind switch {
        "syslog" => "Timestamp:datetime,Hostname:string,AppName:string,ProcId:string,Severity:string,Facility:string,Message:string,Raw:string",
        "auditd" => "Timestamp:datetime,RecordType:string,Serial:long,Node:string,Arch:string,Syscall:string,Success:string,Pid:int,Ppid:int,Uid:int,Auid:int,Exe:string,Comm:string,Key:string,Raw:string",
        "csv" => "",
        "lines" => "lineNumber:long,line:string",
        _ => ""
    };

    private static bool RequiresExternalBinding(string sourceKind, ResourceProfile profile) => !(sourceKind is "eventlog" or "etw") || (string.IsNullOrWhiteSpace(profile.Resource.Channel) && string.IsNullOrWhiteSpace(profile.Resource.Session));

    private static string BuildDisplayName(ResourceProfile profile)
    {
        var table = string.IsNullOrWhiteSpace(profile.Input.Table) ? "Source" : profile.Input.Table;
        var resource = FirstNonWhiteSpace(profile.Resource.Service, profile.Resource.Channel, profile.Resource.Session, profile.Resource.Provider, profile.Resource.Family);
        return $"{table} ({resource})";
    }
}
