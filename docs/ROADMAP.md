# Roadmap

DeltaZulu.Agent has moved from the initial library-and-buffer spike into a working split-host architecture: `dzagentctl` for exploration, `dzagentd` for YAML-configured daemon operation, and `dzagentd` collector mode for local RELP validation. This document is the single planning, status, and validation reference for the agent docs. The architecture details live in [`ARCHITECTURE.md`](ARCHITECTURE.md).


## Documentation consistency assessment

This roadmap now treats documentation alignment as the first planning step before feature expansion. The assessment asked "why" repeatedly and resolved the answers from the current repository shape and docs:

| Question | Answer from the docs/repo | Alignment decision |
| --- | --- | --- |
| Why did docs call `dzagentd` forwarding-role while also describing a collector-style daemon configuration? | `ARCHITECTURE.md` and `README.md` both describe `sources[].input: relp` with console/file output for validation. | Use "production forwarding role" for `dzagentd`; reserve collector-style mode for local validation and controlled labs. |
| Why did docs mention `RELP.Net` and `external/RELP.Net`? | The current `.gitmodules` declares `external/DeltaZulu.Relp` and `external/DeltaZulu.DurableBuffer`; `src/DeltaZulu.Agent.Outputs` references `DeltaZulu.Relp`. | Refer to the transport dependency as `DeltaZulu.Relp` and initialize both direct submodules. |
| Why did the roadmap list Domain/Application/Core projects? | Current project files use `DeltaZulu.Agent.Pipeline` and `DeltaZulu.Agent.Runtime`; no `DeltaZulu.Agent.Core` project is present. | Describe the implemented split by actual project names. |
| Why is pipeline extraction still P0 when stabilization also remains P0? | Extraction changes repository boundaries and can destabilize forwarding; stabilization protects the working data path. | Gate extraction behind explicit readiness tasks and keep forwarding stabilization first in execution order. |
| Why are roadmap bullets ambiguous? | Several sections mix desired outcomes with implementation steps. | Convert near-term work into checkable tasks with ownership, prerequisites, and acceptance criteria. |

### Documentation alignment tasks

- [ ] Keep `README.md`, `ARCHITECTURE.md`, and this roadmap synchronized whenever host roles, submodules, or project boundaries change.
  - Instruction: update all three files in the same change when modifying daemon role language or dependency names.
  - Acceptance: outside this assessment table, `rg "RELP.Net|external/RELP.Net|DeltaZulu.Agent.Core|Domain, Application" README.md docs` returns no active-plan references.
- [ ] Keep local validation instructions tied to actual repository paths.
  - Instruction: validate submodule paths against `.gitmodules` before changing setup commands.
  - Acceptance: every documented `git submodule update` path exists in `.gitmodules`.
- [ ] Keep roadmap tasks executable.
  - Instruction: every P0/P1 roadmap section that requests implementation must include a concrete task list or acceptance criteria.
  - Acceptance: no P0 item is phrased only as an aspiration such as "harden", "improve", or "support" without a testable completion signal.

## Current implementation snapshot

Implemented foundation:

- .NET 10 solution split across Pipeline, Runtime, KQL, Outputs, Daemon, CLI, input adapters, Daemon collector mode, and external Buffer/RELP submodule projects.
- `System.Reactive` based input/runtime flow without custom observable infrastructure.
- Resource-profile YAML model, loader, validation, WMI host conditions, and KQL execution seam using `Microsoft.Rx.Kql`.
- NDJSON file/console sinks for the exploration CLI.
- Syslog TCP/file input with lightweight parsing.
- CSV file input.
- Auditd parser and assembler with malformed-line handling, EOE/PROCTITLE completion, hex PATH decoding, and array handling for repeated record types.
- Windows Event Log, EVTX, ETL, and ETW inputs using the mixed Windows eventing boundary documented in [`docs/adr/0001-windows-eventing-library-boundaries.md`](adr/0001-windows-eventing-library-boundaries.md): TraceEvent for live ETW session ownership, Tx for ETL/EVTX import/replay paths, and narrow P/Invoke for OS primitives.
- Windows Event Log named XML `EventData` extraction as nested payload fields and top-level convenience fields.
- Sample Linux, Windows Event Log, auditd, and ETW profiles.
- Shared application runtime that binds inputs, optional profiles, and outputs.
- Split hosts: `dzagentctl` for exploration, `dzagentd` for YAML-configured daemon operation, and `dzagentd` collector mode for local RELP validation.
- YAML daemon configurations in `config/dzagent.yaml` and `config/dzcollector.yaml` for sources, buffer, RELP endpoints/TLS policy, and diagnostics.
- `DeltaZulu.DurableBuffer` durable chunk storage, checksums, atomic file transitions, retry, backpressure, recovery, metrics, and dead-letter support.
- RELP-neutral delivery records, batches, ACKs, transport port, buffered forwarder sink, DeltaZulu.Relp adapter, ordered endpoint failover groundwork, and forwarder health snapshots.
- Stable `DeliveryId` per delivery envelope for at-least-once server deduplication.
- Metadata fallback injection so user KQL projections cannot accidentally remove delivery identity.
- Host-neutral tests for domain/application behavior, profile loading, KQL seams, syslog, auditd, CSV, NDJSON, forwarder behavior, observability, buffer behavior, and ETW integrity detector/monitor suppression logic.
- Windows-only read-only ETW integrity monitor MVP in `DeltaZulu.Agent.Runtime` for agent self-protection diagnostics. It baselines `ntdll!EtwEventWrite` and `ntdll!NtTraceEvent` in the current process and emits structured findings when common ETW bypass prologue patches are observed.

Not implemented yet:

- Production service lifecycle integration for Windows Service Control Manager and systemd.
- Installer/package guidance.
- Fully validated RELP/TLS certificate callback wiring against production receiver builds.
- Profile hot reload.
- Source checkpoint advancement tied explicitly to durable enqueue.
- Journald input.
- Optional typed resource-local enrichment providers.
- Production wiring from ETW integrity findings into the agent's internal health/security telemetry pipeline.
- Host-gated integration coverage for live auditd, Windows Event Log, EVTX, ETL, ETW, and future journald behavior.

## Agent architecture evolution

DeltaZulu.Agent is transitioning from a monolithic streaming ETL agent into a modular orchestrator. The streaming ETL pipeline (inputs, parsers, KQL profiles, outputs, durable buffering, and RELP delivery) will be extracted into a self-contained submodule called `DeltaZulu.Pipeline`. The agent itself becomes a lightweight watchdog/supervisor service that hosts the pipeline alongside new platform services:

- **DeltaZulu.Pipeline** (submodule): The streaming ETL engine. All resource-native collection, KQL filtering, NDJSON/RELP output, and durable delivery live here. The pipeline owns its own submodule references to `DeltaZulu.DurableBuffer` and `DeltaZulu.Relp`. It is independently buildable and testable.
- **Policy download service**: Downloads resource profiles, certificates, and agent configuration from the management server. Detects configuration drift between intended and applied state.
- **Metrics service**: Sends periodic heartbeat and operational telemetry to the server. Forwards pipeline health metrics (buffer state, source status, delivery counters) alongside agent-level metrics (version, platform, uptime).
- **CMDB-lite inventory service**: Runs scheduled local inventory scans (users, software, browser extensions, hardware, ARP table, network interfaces) and sends snapshot reports as structured log events. The server populates inventory tables from these reports. Inventory data is also available locally as KQL-queryable tables for IOC enrichment.

The `dzagentctl` exploration CLI and `dzagentd` collector configuration validation receiver remain in the agent repository. The CLI references the pipeline submodule for input/profile/KQL exploration.

## Completed forwarder-first outcomes

The old forwarder-first plan is complete and no longer maintained as a separate document. Its implemented outcomes are:

- RELP-neutral `DeliveryRecord`, `DeliveryBatch`, `DeliveryAck`, and transport boundary.
- Buffered forwarder path using `DeltaZulu.DurableBuffer` as the durability/backpressure layer.
- DeltaZulu.Relp-backed transport adapter hidden behind the transport boundary.
- Single `dzagentd` executable with a collector configuration for local validation.
- Stable delivery IDs and metadata preservation across KQL projections.
- Forwarder health observations.
- Split `dzagentctl` exploration CLI and `dzagentd` daemon host.

## Agent integrity monitoring

The ETW integrity monitor is a self-protection diagnostic, not an input adapter. Keep it read-only and scoped to current-process user-mode ETW prologue checks until there is a separate design for broader tamper detection. The added value is defensive visibility: if malware or an injected component patches the agent process's ETW write path, the agent can raise a high-severity internal health/security finding instead of silently trusting that its own telemetry path is intact.

- [ ] Wire `IEtwIntegrityReporter` to the production agent diagnostics/event pipeline.
  - Instruction: emit agent-health/security events such as `AgentIntegrityFinding` with category `EtwUserModePrologueIntegrity`; do not mix these findings into normal endpoint source telemetry.
  - Acceptance: a changed prologue creates one structured high-severity internal event containing function, pattern, baseline/current bytes or hashes, process id, architecture, and baseline source.
- [ ] Validate the native memory reader on target Windows architectures before production enablement.
  - Instruction: test x64 first, then any supported x86/Arm64 runtime, with attention to `MEMORY_BASIC_INFORMATION` layout and readable executable pages.
  - Acceptance: Windows validation notes identify OS, process architecture, resolved `ntdll` functions, and successful clean-baseline operation.



## Tx review: high-ROI ETW hot-path improvements

Decision: do not replace `Microsoft.Diagnostics.Tracing.TraceEvent` with Tx for
live ETW collection. Keep TraceEvent for live session ownership and improve the
DeltaZulu hot path around it. Tx remains useful for offline ETL/EVTX import,
replay concepts, partition-key dispatch, and lazy materialization patterns.

Selected improvements:

| Priority | Improvement | Decision |
|---:|---|---|
| P0 | Complete native ETW envelope fields. | Added `NativeEtwEnvelope`; keep extending Windows mapping as TraceEvent exposes more fields. |
| P0 | Compile native identity filters before payload materialization. | Added `NativeEtwIdentityFilter` primitive; wire profile compilation next. |
| P0 | Decode only selected payload fields. | Added selected TraceEvent payload materializer; profile-driven selection is next. |
| P0 | Bounded callback handoff with counters. | Added counter surface; channel handoff remains implementation work. |
| P1 | Preserve Tx lazy-materialization behavior in ETL input. | Apply native-filter and selected-field pattern to ETL import. |
| P1 | Deterministic virtual-time replay benchmarks. | Add benchmark harness before backend replacement decisions. |
| P2 | Manifest/type-generation experiment. | Defer. |
| P3 | Replace live TraceEvent with native ETW backend. | Defer until benchmarks prove need. |
| Rejected | Replace TraceEvent with Tx for live ETW. | Do not do. |

Definition of done for this improvement track:

- Native ETW identity is emitted for every ETW event.
- Source filters can run before full payload materialization.
- Profiles can select payload fields explicitly.
- Unselected payloads are not decoded by default.
- The ETW callback does not perform full dictionary materialization.
- The callback handoff is bounded and has drop counters.
- ETL import can apply the same filtering model.
- Benchmarks show allocation/event and CPU/event improvements.

## ETW session health and forensic alignment roadmap

DeltaZulu will not implement memory-forensic ETW provider/consumer correlation in
the core collection pipeline. Memory-image analysis that walks undocumented ETW
structures, maps providers/consumers from process memory, or dumps ETL-like
buffers belongs in forensic tooling, not the live agent runtime.

Roadmap placement:

| Priority | Item | Scope | Decision |
|---:|---|---|---|
| P0 | ETW parser and source filters | Provider-specific parsing, native event identity, Kernel-File manifest event IDs, pointer-sized semantic decoding, cheap source-side dropping. | Required for first usable agent. |
| P0 | File identity resolver | `FileObject` / `FileKey` / path correlation with provenance. | Required for usable file telemetry. |
| P0 | Process/thread/image context | PID/TID/process-generation/image/command-line enrichment. | Required for usable logs. |
| P1 | IRP lifecycle correlation | Match FileIO start/end by `Irp`, emit duration/status/missing-event markers. | Required for high-quality FileIO. |
| P1 | Session/filter/parser provenance | Emit profile ID, filter version, parser version, session/provider metadata. | Required for auditability. |
| P1 | Loss and quality counters | ETW loss, parser failures, resolver misses, source-drop counters. | Required for trust and troubleshooting. |
| P2 | ETW session health inventory | Inventory DeltaZulu-owned ETW sessions using supported APIs. | Diagnostic feature. |
| P2 | Active ETW session comparison | Compare expected DeltaZulu sessions against observed runtime state. | Tamper/health signal. |
| P3 | Full system ETW provider/consumer inventory | Enumerate broader ETW sessions/providers/consumers where supported. | Optional diagnostic mode. |
| P3 | Forensic memory alignment | Document how DeltaZulu logs can be reconciled with recovered ETL/memory-buffer evidence. | Incident-response support, not agent runtime. |
| Out of scope | Volatility-style memory walking in agent | Handle-table parsing, `_ETW_REG_ENTRY`, `_ETW_REALTIME_CONSUMER`, ETL buffer dumping. | Forensic tooling only. |

### ETW session health inventory

Add a diagnostic capability that reports the health of DeltaZulu-owned ETW
sessions using supported Windows ETW APIs where possible. It must not parse
kernel memory or walk process handle tables.

Initial scope:

- List DeltaZulu-owned ETW sessions.
- Confirm expected providers are enabled.
- Confirm expected keyword and level configuration.
- Report buffer size and loss counters where available.
- Report session start time and collection mode.
- Report profile, filter, parser, session, and provider provenance.
- Emit periodic `EtwSessionHealth` events.

The portable `EtwSessionHealthSnapshot` model captures the event shape and can be
constructed from `EtwCollectorMetrics`; Windows-specific inventory code can fill
observed provider/session fields later.

### Active ETW session comparison

A later diagnostic profile should compare expected DeltaZulu sessions against
observed runtime state to detect collector misconfiguration, session shutdown,
provider disablement, unexpected loss/counter changes, and troubleshooting or
incident-response timeline gaps. This is collection health monitoring, not
detection logic.

Example future profile shape:

```yaml
schemaVersion: 1
id: windows.etw.session-health
name: Windows ETW session health inventory
version: 0.1.0
enabled: false
mandatory: false
resource:
  platform: windows
  family: etw
  mode: diagnostic
  scope: deltazulu-owned-sessions
output:
  mode: Inventory
  format: ndjson
  metadataEnvelope: true
  eventEnvelope: true
```

### Forensic alignment metadata

The agent does not recover ETW buffers from memory. It emits enough native event
identity and provenance to reconcile live logs with ETL files or forensic ETW
evidence recovered later. Required alignment fields are represented by
`EtwForensicAlignmentMetadata`: `HostId`, `TimestampUtc`, `TimestampQpc`,
`ProviderGuid`, `ProviderName`, `EventId`, `Opcode`, `Version`, `ProcessId`,
`ThreadId`, `ActivityId`, `RelatedActivityId`, `EtwSessionName`, `EtwProfileId`,
`EtwProfileVersion`, `ParserName`, `ParserVersion`, `SchemaVersion`, and
`RawPayloadHash`.

## P0: Stabilize daemon forwarding

Goal: protect the working production forwarding path before starting extraction or agent-management services.

- [ ] Keep `dzagentd` focused on configured long-running daemon work: sources, optional profiles, durable enqueue, RELP dispatch, diagnostics, and lab-only collector-style validation.
  - Instruction: do not add inline query, schema listing, table rendering, or ad-hoc export commands to `dzagentd`; keep those in `dzagentctl`.
  - Acceptance: README and architecture host-role text continue to separate CLI exploration from daemon configuration.
- [ ] Exercise daemon smoke tests against `dzagentd` collector mode.
  - Instruction: cover successful send, retry, permanent failure, dead-letter, and restart-recovery scenarios.
  - Acceptance: a documented smoke-test transcript identifies the daemon config, collector command, observed ACK/retry/dead-letter behavior, and cleanup commands.
- [ ] Preserve delivery metadata outside user-controlled KQL projections.
  - Instruction: any change to KQL projection, runtime binding, or delivery serialization must verify agent/source/profile identity survives when `_metadata` is omitted by the query.
  - Acceptance: host-neutral tests prove forwarded records retain collector id, source type/name, profile id/version, and stable delivery id material.
- [ ] Tie future source checkpoint advancement to durable enqueue rather than network ACK.
  - Instruction: checkpoint only after the source event has been represented in durable buffer state; do not wait for RELP ACK before source advancement.
  - Acceptance: design notes and tests distinguish source checkpointing from transport acknowledgement.
- [ ] Keep operator examples aligned with `config/dzagent.yaml`.
  - Instruction: update examples in README and this roadmap whenever daemon config keys or default buffer paths change.
  - Acceptance: smoke-test examples run from a clean checkout and include commands to remove temporary logs, configs, and buffer directories.

## P0: Validation and compatibility

- [ ] Re-run restore, build, and tests whenever SDK versions, dependencies, Windows input adapters, KQL behavior, daemon configuration, or forwarder transport behavior change.
  - Acceptance: PR notes list the exact restore/build/test commands and distinguish host-neutral checks from Windows-only checks.
- [ ] Keep host-neutral unit and fixture tests fast and deterministic.
  - Instruction: prefer literal fixtures, temporary directories, and in-memory transports over live host services.
  - Acceptance: Linux/macOS validation does not require auditd, journald, Windows Event Log, EVTX, ETL, or ETW availability.
- [ ] Validate Windows Event Log, EVTX, ETL, and ETW behavior on Windows hosts when changing Windows adapters or profiles.
  - Acceptance: Windows validation notes identify the host, available logs/providers, and any optional resources skipped.
- [ ] Monitor `Microsoft.Rx.Kql`, `Tx.Windows`, and `DeltaZulu.Relp` compatibility with .NET 10.
  - Instruction: when upgrading any of these dependencies, add one focused compatibility note or regression test for the changed surface.
  - Acceptance: dependency changes do not merge with only transitive build success as evidence.
- [ ] Add targeted golden fixtures only when they protect a real regression or newly supported source behavior.
  - Acceptance: each new fixture states the source behavior or regression it locks down.

### Project-by-project test focus

| Project | Current test focus | High-ROI scenarios | Deferred coverage |
| --- | --- | --- | --- |
| `DeltaZulu.Agent.Pipeline` | Pipeline records, dictionary helpers, profile validation/YAML loading, delivery identity, and serialization helpers. | Dictionary coercion, metadata preservation, delivery identity, serialization helpers, and profile validation/loading. | Additional compatibility tests only when older consumers require them. |
| `DeltaZulu.Agent.Inputs/Auditd` | Parser and assembler business logic using literal audit lines. | Audit line prefix validation, scalar coercion, hex argument decoding, record grouping by audit ID, ordered `ARGV`, multi-record fields such as `PATH`, and one-shot flush behavior. | Reading `/var/log/audit/audit.log`, tailing files, or interacting with auditd. |
| `DeltaZulu.Agent.Inputs/Syslog` | Dependency-free parser behavior using literal RFC 3164, RFC 5424, and unstructured messages. | Priority decoding, hostname/process extraction, source address preservation, key/value extraction, and raw-message preservation. | TCP listener behavior, journald, syslog daemon configuration, and socket-level error handling. |
| `DeltaZulu.Agent.Inputs/Files` | CSV row-to-event behavior using temporary files and culture-invariant parsing. | File roundtrip behavior, type coercion, malformed row diagnostics, and long-running file scenarios if added. | Filesystem watcher behavior. |
| `DeltaZulu.Agent.Inputs/Windows` | Host-neutral mapping helpers, named EventData extraction, and nested field exposure. | More XML mapping variants and optional provider fixtures. | ETW sessions, live Windows Event Log subscriptions, and EVTX/ETL host dependencies. |
| `DeltaZulu.Agent.Kql` | In-memory profile execution, no-match/error behavior, metadata fallback injection, and nested field access. | Source-family-specific nested field probes as profiles mature. | Query engine internals and package behavior outside the repository boundary. |
| `DeltaZulu.Agent.Pipeline` (`Ndjson`, `MessagePack`, `Relp` namespaces) | Shared parser/serializer and transport-framing business logic. | Property-name preservation, null omission, compact single-line JSON settings, MessagePack payload roundtrips, and RELP frame behavior. | Output sink I/O failures and file permission integration cases. |
| `DeltaZulu.Agent.Outputs` (`Ndjson`/`Relp` namespaces) | Output sink behavior and RELP forwarding. | Exception-to-error mapping, console/file output behavior, buffered RELP health and delivery behavior. | Filesystem/network integration beyond deterministic tests. |
| `DeltaZulu.DurableBuffer` | Durable chunking, state transitions, retry, backpressure, metrics, recovery, and integration flows. | Write-dispatch-ACK, flush-on-stop, permanent failure dead-lettering, record-too-large rejection, checksum/corruption behavior, and option defaults. | Filesystem and disk-pressure behavior beyond deterministic temp-directory tests. |

### Required local validation

Run validation from the repository root on a host with the .NET 10 SDK installed. The repository targets `net10.0` and includes Windows-only inputs that target `net10.0-windows`, so Linux/macOS validation should cover host-neutral projects while Windows validation should additionally cover Event Log, EVTX, ETL, and ETW behavior.

Confirm the SDK and initialize direct submodules when needed:

```bash
dotnet --list-sdks
git submodule update --init --recursive external/DeltaZulu.Relp external/DeltaZulu.DurableBuffer
```

Run host-neutral validation on any .NET 10-capable host:

```bash
dotnet restore DeltaZulu.Agent.slnx
dotnet build DeltaZulu.Agent.slnx --no-restore
dotnet test tests/DeltaZulu.Agent.Tests/DeltaZulu.Agent.Tests.csproj --no-build
dotnet test tests/DeltaZulu.DurableBuffer.Tests/DeltaZulu.DurableBuffer.Tests.csproj --no-build
```

Run Windows-specific checks on a Windows host when changing Windows input adapters or profiles:

```powershell
dotnet restore DeltaZulu.Agent.slnx
dotnet build DeltaZulu.Agent.slnx --no-restore
dotnet test tests/DeltaZulu.Agent.Tests/DeltaZulu.Agent.Tests.csproj --no-build
dotnet run --project src/DeltaZulu.Agent.Cli -- schemas profiles table
dotnet run --project src/DeltaZulu.Agent.Cli -- eventlog Security table --kql "Source | where EventId == 4688 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"
```

Use optional or lab-only resources for Sysmon, PowerShell, SMB, and Defender examples unless those logs are guaranteed to exist on the validation host.

### Forwarder and daemon collector smoke test

Use two terminals on a .NET 10-capable host:

```bash
# terminal 1
dotnet run --project src/DeltaZulu.Agent.Daemon -- config/dzcollector.yaml

# terminal 2
printf '<34>1 2026-06-23T12:00:00Z host app 123 ID47 - test message\n' > /tmp/dzagent-smoke.log
cp config/dzagent.yaml /tmp/dzagentd-smoke.yaml
python3 - <<'PYAML'
from pathlib import Path
p = Path('/tmp/dzagentd-smoke.yaml')
s = p.read_text().replace('/var/log/auth.log', '/tmp/dzagent-smoke.log')
s = s.replace('./buffer/agentd', './buffer/agentd-smoke')
p.write_text(s)
PYAML
dotnet run --project src/DeltaZulu.Agent.Daemon -- --config /tmp/dzagentd-smoke.yaml
```

After the smoke test, remove temporary files and buffer state:

```bash
rm -f /tmp/dzagent-smoke.log
rm -f /tmp/dzagentd-smoke.yaml
rm -rf ./buffer/agentd-smoke
```

Daemon collector mode is only a local validation receiver. It is not a production collector, SIEM, or syslog daemon replacement.

## P0: Pipeline extraction readiness

Do not start extraction until daemon forwarding stabilization tasks are passing and the repository-boundary decision is re-confirmed. Extract the streaming ETL engine into a standalone `DeltaZulu.Pipeline` repository and wire it as a git submodule under `external/DeltaZulu.Pipeline`.

### Projects to extract

The former `DeltaZulu.Agent.Domain` and `DeltaZulu.Agent.Application` responsibilities have been split into `DeltaZulu.Agent.Pipeline` and `DeltaZulu.Agent.Runtime` projects. `DeltaZulu.Agent.Pipeline` owns ETL pipeline input/output/filter/serialization/parsing models and helpers, while `DeltaZulu.Agent.Runtime` owns daemon/CLI orchestration. If extraction resumes, these projects are the source boundaries for future `DeltaZulu.Pipeline.*` packages.

| Current project/folder | Pipeline project | Content |
| --- | --- | --- |
| `DeltaZulu.Agent.Pipeline` | `DeltaZulu.Pipeline.Shared` | SourceEvent, ResourceOutputRecord, DeliveryRecord, ResourceProfile, observations, YAML profile loading, profile validation, completion tracking, output multiplexing, and shared serialization/framing helpers. |
| `DeltaZulu.Agent.Runtime` | `DeltaZulu.Pipeline.Application` | AgentRuntime and ProfileBinding orchestration that bind inputs, profiles, executors, and sinks. |
| `DeltaZulu.Agent.Inputs` | `DeltaZulu.Pipeline.Inputs` | All input adapters: syslog, CSV, auditd, Windows Event Log, EVTX, ETL, ETW, RELP. |
| `DeltaZulu.Agent.Kql` | `DeltaZulu.Pipeline.Kql` | ResourceKqlProfileExecutor and custom scalar functions. |
| `DeltaZulu.Agent.Outputs` | `DeltaZulu.Pipeline.Outputs` | NDJSON console/file sinks, buffered RELP sink, transport adapter, health reporting. |

### Submodule wiring

The pipeline repository owns its own submodule references to `DeltaZulu.DurableBuffer` and `DeltaZulu.Relp`. The agent repository references the pipeline as `external/DeltaZulu.Pipeline` and transitively gets buffer and RELP through it. The agent's direct submodule references to DurableBuffer and Relp are removed.

### What stays in the agent

- `DeltaZulu.Agent.Daemon`: Refactored from a production-forwarding daemon into the orchestrator/watchdog. Supervises the pipeline, policy service, metrics service, and inventory service as managed hosted services.
- `DeltaZulu.Agent.Cli`: Exploration CLI. References the pipeline submodule for input/profile/KQL operations.
- `DeltaZulu.Agent.Daemon collector config`: Local RELP validation receiver.
- New agent-level projects for policy, metrics, and inventory services.

### Test split

Pipeline-specific unit tests (domain, inputs, KQL, outputs, profiles, NDJSON serialization) move into the pipeline repository. Integration tests that exercise the full agent (daemon startup, end-to-end forwarding, CLI smoke tests) stay in the agent repository. Buffer tests remain with `DeltaZulu.DurableBuffer`.

### Readiness tasks

- [ ] Confirm `DeltaZulu.Agent.Pipeline` and `DeltaZulu.Agent.Runtime` are the only source boundaries for extraction.
- [ ] Decide whether `DeltaZulu.DurableBuffer` remains a direct agent submodule during transition or moves exclusively under `DeltaZulu.Pipeline` after extraction.
- [ ] Produce a branch plan that keeps the current agent build green before deleting in-repo pipeline projects.
- [ ] Define package/project names for extracted test projects before moving files.
- [ ] Update README and architecture diagrams in the same branch as the extraction.

### Migration sequence

1. Create the `DeltaZulu.Pipeline` repository with the extracted projects, solution file, build props, and test projects.
2. Verify the pipeline builds and tests pass independently.
3. Add `DeltaZulu.Pipeline` as a submodule under `external/DeltaZulu.Pipeline` in this repository.
4. Update the agent solution to reference pipeline projects from the submodule path.
5. Remove the original pipeline project directories from `src/`.
6. Remove direct DurableBuffer and Relp submodule references from the agent (they come through Pipeline).
7. Update `DeltaZulu.Agent.Daemon` to use pipeline types from the submodule.
8. Update `DeltaZulu.Agent.Cli` to use pipeline types from the submodule.
9. Verify the agent builds, tests pass, and the daemon/CLI/collector smoke tests work.

## P1: Policy download service

Add a hosted service that synchronizes agent configuration with the management server:

- Download resource profiles from the server and place them in the local profile directory.
- Download and rotate agent certificates as part of the automated enrollment lifecycle.
- Sync buffer, RELP, and diagnostics configuration from the server-assigned policy.
- Report the applied policy version and configuration hash so the server can detect drift.
- Respect local overrides where the operator has pinned specific settings.
- Retry with backoff on transient failures; continue with last-known-good configuration when the server is unreachable.

This service implements the agent side of the declarative policy model, policy assignment, and effective configuration reporting described in the Agent Management Roadmap.

## P1: Metrics service

Add a hosted service that sends periodic heartbeat and operational telemetry to the management server:

- Send heartbeat at a configured interval with agent version, platform, OS metadata, policy hash, and agent status.
- Forward pipeline health metrics: per-source status (enabled channels, read errors, lag, last event timestamp), buffer state (memory queue, disk queue, dropped, retried, backpressure), and delivery counters (written, sent, acknowledged, dead-lettered).
- Report agent-level metrics: CPU and memory usage, uptime, service restart count.
- Report last successful send timestamp so the server can distinguish healthy, degraded, stale, and offline agents.

This service implements the agent side of the heartbeat endpoint, source health reporting, and buffer health reporting described in the Agent Management Roadmap.

## P1: Production RELP and TLS hardening

- Continue hardening the DeltaZulu.Relp adapter behind the RELP-neutral transport boundary.
- Validate plain RELP and RELP/TLS wire behavior against production rsyslog and syslog-ng builds.
- Wire configured certificate policy into server-certificate validation when supported by the underlying DeltaZulu.Relp surface.
- Add certificate-expiry diagnostics and clearer TLS failure reporting.
- Refine endpoint selection and transient/permanent failure classification while leaving durable retry scheduling in `DeltaZulu.DurableBuffer`.
- Keep `docs/RELP_RECEIVER_SETUP.md` aligned with validated receiver behavior.

## P1: Operations

- Add service lifecycle integration for Windows Service Control Manager and systemd.
- Add installer/package guidance after daemon lifecycle behavior is stable.
- Add profile/config validation commands or startup diagnostics suitable for operators.
- Add profile hot reload after source checkpoint and forwarding semantics are clear.
- Expand health output into the eventual operational telemetry contract: written, sent, acknowledged, retried, dead-lettered, rejected, disk usage, and oldest buffered age.

## P2: CMDB-lite inventory service

Add a scheduled inventory collection service that captures discrete endpoint state and sends it to the server for inventory table population. Inventory data is not a stream; it is a periodic snapshot. Each collector runs on a configurable schedule and produces a structured report.

### Inventory collectors

| Collector | Platform | Description |
| --- | --- | --- |
| Local users and groups | Windows, Linux | Enumerate local accounts, group memberships, and account status. |
| Installed software | Windows, Linux | Installed programs (Windows registry, dpkg/rpm/snap). |
| Browser extensions | Windows, Linux | Extensions for Chrome, Edge, Firefox from known profile paths. |
| Hardware inventory | Windows, Linux | CPU, memory, disk, BIOS/firmware, serial numbers via WMI/sysfs/dmidecode. |
| ARP table | Windows, Linux | Current ARP cache for neighbor discovery context. |
| Network interfaces | Windows, Linux | Interface names, addresses, MAC addresses, link state, DNS configuration. |

### Design principles

- Inventory collectors are discrete-state, not streaming. They use a scan-and-report pattern with their own scheduling, separate from the reactive pipeline.
- Each scan produces a structured snapshot report serialized as a structured log event and sent through the agent's delivery path (durable buffer and RELP).
- The server populates inventory tables from these reports, maintaining current and historical state.
- Inventory data is also exposed locally as KQL-queryable tables, enabling local enrichment and IOC joining without server round-trips.
- Collectors must be safe and read-only. They enumerate existing state; they never modify the endpoint.
- Platform-specific collectors are conditionally compiled or runtime-gated so the agent builds and runs on both Windows and Linux.

### Relationship to Agent Management Roadmap

The CMDB-lite inventory service implements the local context capabilities described in the Agent Management Roadmap: diagnostic providers (P2) and local enrichment providers (P2). Installed software, browser extensions, local users, and selected system facts become available for both server-side inventory and agent-side enrichment.

## Agent Management Roadmap

The agent management capability will provide centralized lifecycle control for DeltaZulu agents without turning the agent into a general-purpose remote execution platform. The priority is to make collection reliable, observable, configurable, and safe across tenants and agent groups. The roadmap starts with identity, enrollment, heartbeat, policy delivery, and operational health, then expands into controlled diagnostics, staged rollout, local enrichment, and upgrade orchestration.

| Priority | Phase | Capability | Description | Outcome |
| --- | --- | --- | --- | --- |
| P0 | Foundation | Agent identity model | Define `TenantId`, `AgentId`, certificate identity, hostname, OS metadata, agent version, and first/last seen timestamps. | Every agent has a stable, auditable identity. |
| P0 | Foundation | Automated enrollment | Implement bootstrap-token enrollment that exchanges a short-lived onboarding token for a tenant-scoped agent identity and certificate. | New customer onboarding can automatically provision agents without manual PKI work. |
| P0 | Foundation | Heartbeat endpoint | Agents periodically report version, platform, policy hash, source status, buffer state, output state, and last successful send time. | The platform can distinguish healthy, degraded, stale, and offline agents. |
| P0 | Foundation | Agent inventory view | Build a basic UI/API view listing agents by tenant, group, OS, version, status, last seen, and assigned policy. | Operators can see the managed fleet and identify coverage gaps. |
| P0 | Configuration | Declarative policy model | Define agent policies for inputs, channels, filters, buffer settings, RELP/TLS output, compression, and heartbeat intervals. | Agent behavior becomes centrally defined and versioned. |
| P0 | Configuration | Policy assignment | Allow policies to be assigned to tenants, groups, or individual agents. | Different server classes, environments, and tenants can use different collection profiles. |
| P0 | Configuration | Effective configuration reporting | Agents report the exact applied policy version and configuration hash. | The platform can detect configuration drift between intended and applied state. |
| P0 | Reliability | Source health reporting | Agents report per-source status, including enabled channels, read errors, bookmark state, lag, and last event timestamp. | Collection failures become visible before detection coverage is affected. |
| P0 | Reliability | Buffer health reporting | Agents report memory queue, disk queue, dropped records, retry count, and backpressure state. | Operators can detect forwarding, storage, and network pressure. |
| P1 | Operations | Command queue | Add a constrained command model for one-shot operational actions such as reload configuration, test output, flush buffer, collect diagnostics, and restart service. | Operators can remediate common agent issues centrally. |
| P1 | Operations | Command result store | Persist command status, timestamps, structured output, errors, timeout state, and requesting user. | Operational actions become auditable and supportable. |
| P1 | Safety | Command allowlist | Restrict remote commands to predefined safe operations. Avoid arbitrary shell, arbitrary script execution, or unrestricted local queries. | Agent management remains operationally useful without becoming an unsafe remote execution channel. |
| P1 | Configuration | Policy validation | Validate policy syntax, allowed inputs, buffer limits, endpoint settings, and incompatible options before rollout. | Bad configuration is rejected before it reaches endpoints. |
| P1 | Configuration | Canary rollout | Support assigning a new policy version to a small group before wider deployment. | Configuration risk is reduced during changes. |
| P1 | Configuration | Rollback | Preserve previous policy versions and allow rollback at tenant, group, or agent level. | Failed rollouts can be reversed quickly. |
| P1 | Monitoring | Fleet health dashboard | Aggregate agent health by tenant, group, OS, version, policy, source status, and buffer pressure. | Operators can prioritize remediation at fleet level instead of reviewing agents one by one. |
| P1 | Monitoring | Drift and stale-agent detection | Flag agents that stop checking in, run old versions, fail to apply policy, or report unexpected source state. | Silent degradation becomes visible. |
| P2 | Security | Certificate lifecycle automation | Automate certificate issuance, renewal, revocation, expiry monitoring, and rotation. | PKI remains a backend concern and does not burden customers. |
| P2 | Security | Tenant isolation controls | Enforce tenant-scoped agent identity, tenant-scoped policies, tenant-scoped commands, and tenant-scoped results. | Cross-tenant management mistakes are prevented by design. |
| P2 | Security | Management-plane audit log | Record enrollment, policy changes, command requests, command results, certificate actions, and administrative access. | Agent management actions become reviewable and compliance-friendly. |
| P2 | Local context | Diagnostic providers | Add predefined local checks for service status, event-channel availability, Sysmon channel presence, agent permissions, disk state, and network reachability. | Support teams can diagnose common endpoint issues without logging into the host. |
| P2 | Local context | Local enrichment providers | Add controlled providers for installed software, drivers, browser extensions, local users, and selected system facts. | Agent-collected telemetry can be enriched without requiring a separate endpoint inventory product. |
| P2 | Data quality | Telemetry utilization metrics | Report collected, filtered, forwarded, dropped, retried, and suppressed event counts by source and policy. | The platform can measure collection quality and wasted telemetry. |
| P2 | Data quality | Collection recommendation engine | Use deterministic health and utilization metrics to suggest source, filter, and policy improvements. | Operators receive actionable recommendations instead of raw health signals only. |
| P3 | Lifecycle | Agent upgrade orchestration | Support version rings, signed package validation, staged rollout, upgrade status, and rollback. | Agent upgrades become centrally controlled and observable. |
| P3 | Lifecycle | Compatibility matrix | Track which agent versions support which policy schema versions, input types, commands, and protocol features. | Rollouts avoid incompatible configuration and protocol combinations. |
| P3 | Advanced operations | Maintenance windows | Allow policy rollout, upgrade, and disruptive commands to respect tenant or group maintenance windows. | Operational changes align with customer change-control requirements. |
| P3 | Advanced operations | Advanced local query abstraction | Expose selected local resource providers through a constrained query interface with limits, RBAC, audit, and timeout controls. | Analysts and operators can inspect endpoint context safely without arbitrary endpoint execution. |
| P3 | Advanced operations | Multi-region management support | Support regional management endpoints and tenant affinity for larger deployments. | The management plane can scale across customer regions and data-residency boundaries. |

The implementation order should start with P0 identity, enrollment, heartbeat, policy assignment, and health reporting. These capabilities create the minimum viable control plane. P1 should then add constrained operations, rollout safety, and fleet-level visibility. P2 should improve security, auditability, local context, and telemetry-quality measurement. P3 should focus on mature lifecycle management, controlled local querying, maintenance windows, and scale.

## P2: Source and profile expansion

- Harden auditd assembler behavior further only where parser correctness requires it; keep LAUREL-level enrichment post-1.0.
- Add host-gated integration tests for auditd, Windows eventing, ETW/ETL/EVTX, and future journald support.
- Add journald input.
- Validate nested KQL access for auditd and other non-Windows source families when those profiles resume.
- Add typed resource-local enrichment providers after version 1.0 is stable and only where they do not perform server-canonical normalization.

## P2: Future ideas borrowed from Fluent Forward

DeltaZulu will not use Fluent Forward as the native agent protocol, but its ecosystem suggests useful design ideas to revisit after the RELP path is stable:

- Add explicit chunk or batch identifiers and acknowledgement correlation so retries are easy to reason about and server-side deduplication has stable keys.
- Keep a first-class event envelope with routing metadata separate from user-controlled event payload fields; tags are useful for routing, but tenant and agent identity must come from certificates and validated delivery metadata.
- Support efficient packed batches once single-record and chunk retry behavior is proven, with documented limits for batch size, age, and compression.
- Consider payload compression as a negotiated or configured delivery-envelope feature after interoperability and observability are mature.
- Design endpoint failover and load-balancing behavior deliberately, including health checks, backoff, endpoint quarantine, and clear operator diagnostics.
- Preserve collector interoperability as a platform-ingress concern, not an agent transport requirement, so future adapters can be added without expanding the native agent protocol surface.

## P3: Deferred reactive runtime evaluation

Treat migration from `System.Reactive` to R3 as a lowest-priority performance improvement candidate, not a committed migration. Revisit only after higher-priority agent, pipeline, management, and operational work is stable, and only if profiling shows the current reactive runtime is a measurable bottleneck.

- Start with a limited pilot on one low-risk input handler, such as CSV file input or FIFO/syslog input, before any broad pipeline changes.
- Benchmark memory pressure, per-event latency, subscription overhead, and long-running stability against the existing `System.Reactive` implementation using production-like event volumes.
- Require a clear go/no-go threshold, such as at least a 10% improvement in memory or latency, before planning a full migration.
- Account for R3 breaking changes around `IObservable<T>`/`IObserver<T>` replacement, `Observable.Create` signatures, completion/error semantics, and custom observer implementations.
- Validate common operator behavior, error paths, and high-volume input scenarios with the full test suite and targeted integration/load tests.
- Keep `System.Reactive` if the pilot shows no meaningful bottleneck, if behavior differs in risky ways, or if migration would distract from higher-priority reliability and management work.

## Architecture discipline

- Keep `DeltaZulu.Pipeline` self-contained: it must build, test, and run independently of the agent orchestrator.
- Keep the agent as an orchestrator/watchdog only: it supervises pipeline, policy, metrics, and inventory services but does not contain streaming ETL logic.
- Keep `DeltaZulu.DurableBuffer` as the authoritative durability and backpressure layer.
- Keep DeltaZulu.Relp details behind the forwarder transport adapter.
- Keep `dzagentctl` and `dzagentd` responsibilities separate.
- Keep input adapters source-native and avoid broad architecture-only reshuffles.
- Keep inventory collectors discrete-state with scan-and-report scheduling, separate from the reactive streaming pipeline.
- Preserve original resource field names and defer semantic normalization to the server.

## Permanently out of scope

- DuckDB.
- SQL window engines.
- Edge-side server-canonical normalization.
- A built-in production syslog daemon replacement.
