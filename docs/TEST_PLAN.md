# DeltaZulu.Agent Test Plan

This repository validates business logic with fast unit tests and avoids mocks for operating-system components such as auditd, journald, ETW, and event logs. OS integrations should be covered later by opt-in integration tests that run only on compatible hosts with fixture files or real services.

## Project-by-project plan

| Project | Current test focus | High-ROI scenarios | Deferred coverage |
| --- | --- | --- | --- |
| `DeltaZulu.Agent.Core` | Pure event and dictionary helpers. | Dictionary coercion preserves case-insensitive field access, safely handles nullable values, and emits KQL-ready dictionaries without nulls. | Reactive pipeline lifecycle tests with in-memory observables and sinks. |
| `DeltaZulu.Agent.Inputs.Auditd` | Parser and assembler business logic using literal audit lines. | Audit line prefix validation, scalar coercion, hex argument decoding, record grouping by audit ID, ordered `ARGV`, multi-record fields such as `PATH`, and one-shot flush behavior. | Reading `/var/log/audit/audit.log`, tailing files, or interacting with auditd. |
| `DeltaZulu.Agent.Inputs.Syslog` | Dependency-free parser behavior using literal RFC 3164, RFC 5424, and unstructured messages. | Priority decoding, hostname/process extraction, source address preservation, key/value extraction, and raw-message preservation. | TCP listener behavior, journald, syslog daemon configuration, and socket-level error handling. |
| `DeltaZulu.Agent.Inputs.Files` | Not implemented in this pass. | CSV row-to-event behavior using temporary files and culture-invariant parsing. | Filesystem watcher behavior and long-running tail behavior. |
| `DeltaZulu.Agent.Inputs.Windows` | Not implemented in this pass because the project targets `net10.0-windows` and depends on Windows eventing packages. | Pure mapping helpers such as `WindowsSourceEventMapper` and dictionary extension behavior. | ETW sessions, live Windows Event Log subscriptions, and EVTX/ETL host dependencies. |
| `DeltaZulu.Agent.Kql` | Not implemented in this pass because it depends on the KQL runtime package. | Scalar helper behavior and profile executor no-match/error behavior with in-memory events. | Query engine internals and package behavior outside the repository boundary. |
| `DeltaZulu.Agent.Outputs.Ndjson` | Serializer and error-record business logic. | Property-name preservation, null omission, compact single-line JSON settings, and exception-to-error mapping. | Console/file sink I/O failures and file permission integration cases. |
| `DeltaZulu.Agent.Profiles` | Profile validation business rules. | Minimal valid profiles, unsupported languages/formats, required fields, preservation requirements, and source-aware exception messages. | Full YAML fixture loading across every bundled production profile. |

## Execution strategy

1. Keep unit tests deterministic and host-neutral; tests should run without auditd, journald, ETW, event logs, or privileged services.
2. Keep unit coverage in the single `DeltaZulu.Agent.Tests` MSTest project, with one test class per source project or domain area.
3. Prefer literal representative log/profile samples for parser and validator tests.
4. Add fixture-based integration tests later under a separate naming convention or trait so CI can opt in explicitly.
5. Prioritize tests around field preservation, normalization boundaries, and error messages because these define compatibility with downstream ResourceQL processing.

## DeltaZulu.Buffer test coverage

Tests are in `tests/DeltaZulu.Buffer.Tests/` using MSTest.

| Test class | Focus |
| --- | --- |
| `ChunkBuilderTests` | Append, record count, rotation by count/age, seal validation, checksum roundtrip, reset, byte limit checks. |
| `ChunkFormatTests` | Checksum validation, corruption detection, record reading, invalid magic, empty chunk. |
| `BackpressureControllerTests` | All four states (Healthy/Degraded/Pressured/Full), boundary values, precedence rules. |
| `BufferMetricsCounterTests` | Counter increments, gauge updates, snapshot completeness, `AddDiskBytes` with positive/negative values. |
| `BufferEventBroadcasterTests` | Subscribe/unsubscribe, multiple observers, faulty observer isolation, completion notification. |
| `ExponentialBackoffRetrySchedulerTests` | Delay increase, max delay cap, retry exhaustion. |
| `FileChunkStoreTests` | Directory creation, seal/move/delete lifecycle, metadata JSON roundtrip, disk usage, quarantine, orphan handling. |
| `JsonRecordSerializerTests` | UTF-8 output, naming policy, dictionary serialization. |
| `BufferIntegrationTests` | End-to-end write-dispatch-ACK, flush on stop, event observation, rejected-stopping, permanent failure dead-lettering, record-too-large. |
| `BufferWriteResultTests` | `IsAccepted` for each status. |
| `BufferSnapshotTests` | Property preservation. |
| `BufferEventTests` | Create with/without chunk, timestamp presence. |
| `ChunkIdTests` | Uniqueness, equality, ToString. |
| `ChunkMetadataTests` | JSON round-trip. |
| `ChunkSendResultTests` | Status and error. |
| `RecoverySummaryTests` | Property preservation. |
| `DeltaZuluBufferOptionsTests` | Defaults and custom values. |

## Required local validation before merge

Run validation from the repository root on a host with the .NET 10 SDK installed. The repository targets `net10.0` and includes Windows-only inputs that target `net10.0-windows`, so Linux/macOS validation should cover host-neutral projects while Windows validation should additionally cover Event Log, EVTX, ETL, and ETW behavior.

### Prerequisites

1. Confirm the SDK:

   ```bash
   dotnet --list-sdks
   ```

   The output must include a .NET 10 SDK.

2. Initialize the RELP.Net submodule when `external/RELP.Net` is empty or missing package assets:

   ```bash
   git submodule update --init --recursive external/RELP.Net
   ```

### Host-neutral validation

Run these commands on any .NET 10-capable host:

```bash
dotnet restore DeltaZulu.Agent.sln
dotnet build DeltaZulu.Agent.sln --no-restore
dotnet test tests/DeltaZulu.Agent.Tests/DeltaZulu.Agent.Tests.csproj --no-build
dotnet test tests/DeltaZulu.Buffer.Tests/DeltaZulu.Buffer.Tests.csproj --no-build
```

These commands validate the host-neutral parser, profile, NDJSON, KQL seam, forwarder contract, and buffer coverage. If a non-Windows host cannot build Windows-targeted projects, rerun the host-neutral test projects directly and record the Windows target limitation in the validation notes.

### Windows-specific validation

Run these checks on a Windows host with the .NET 10 SDK when changing Windows input adapters or profiles:

```powershell
dotnet restore DeltaZulu.Agent.sln
dotnet build DeltaZulu.Agent.sln --no-restore
dotnet test tests/DeltaZulu.Agent.Tests/DeltaZulu.Agent.Tests.csproj --no-build
dotnet run --project src/DeltaZulu.Agent.Cli -- schemas profiles table
dotnet run --project src/DeltaZulu.Agent.Cli -- eventlog Security table --kql "Source | where EventId == 4688 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"
```

Use optional or lab-only resources for Sysmon, PowerShell, SMB, and Defender examples unless those logs are guaranteed to exist on the validation host.

### Forwarder and demo collector smoke test

Use two terminals on a .NET 10-capable host:

```bash
# terminal 1
dotnet run --project src/DeltaZulu.Demo.Collector -- --address 127.0.0.1 --port 6514

# terminal 2
printf '<34>1 2026-06-23T12:00:00Z host app 123 ID47 - test message\n' > /tmp/dzagent-smoke.log
dotnet run --project src/DeltaZulu.Agent.Cli -- syslog /tmp/dzagent-smoke.log forwarder config/forwarder.yaml --diagnostic-interval 1
```

After the smoke test, remove the temporary log and buffer directory if the run completed successfully:

```bash
rm -f /tmp/dzagent-smoke.log
rm -rf ./buffer/forwarder
```

The demo collector is only a local validation receiver. It is not a production collector, daemon, SIEM, or syslog daemon replacement.

## Windows Event Log field extraction notes

Windows Event Log records expose two layers of fields to profiles:

- Transport and envelope fields such as `ProviderName`, `EventId`, `Channel`, `RecordId`, `Level`, `Keywords`, `MachineName`, `ProcessId`, `ThreadId`, `TimeCreated`, `Message`, and `RawEvent`.
- Named event payload fields parsed from the XML `<EventData><Data Name="...">...</Data></EventData>` block. When names are available, the adapter exposes them twice: as members of the dynamic `EventData` object and as top-level convenience fields so existing profiles can filter with either `EventData.TargetUserSid` or `TargetUserSid`.

Common profile examples:

```kql
// Security 4688 process creation
Source
| where EventId == 4688
| project TimeCreated, Computer=MachineName, NewProcessName, ParentProcessName, SubjectUserSid, SubjectLogonId, CommandLine, _metadata

// Sysmon Event ID 1 process creation
Source
| where ProviderName == "Microsoft-Windows-Sysmon" and EventId == 1
| project UtcTime, ProcessGuid, ProcessId, Image, CommandLine, ParentProcessGuid, ParentImage, User, Hashes, _metadata

// PowerShell script block logging
Source
| where Channel has "PowerShell" and EventId in (4103, 4104)
| project TimeCreated, ProviderName, EventId, ScriptBlockText, Path, UserId=ContextInfo, _metadata

// SMB share access signal
Source
| where Channel == "Security" and EventId == 5140
| project TimeCreated, SubjectUserName, SubjectUserSid, ShareName, ShareLocalPath, IpAddress, _metadata

// Defender threat detection signal
Source
| where ProviderName has "Windows Defender" and EventId in (1116, 1117, 5007)
| project TimeCreated, EventId, ThreatName, SeverityName, Path, User, Message, _metadata
```

Keep host-neutral tests focused on XML-to-dictionary mapping helpers. Validate live channel differences on Windows because providers can omit names, localize messages, or require optional components such as Sysmon, PowerShell Operational logging, SMB auditing, or Defender.
