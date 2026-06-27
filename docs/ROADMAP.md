# Roadmap

DeltaZulu.Agent has moved from the initial library-and-buffer spike into a working split-host architecture: `dzagentctl` for exploration, `dzagentd` for forwarder-only daemon operation, and `dzdemo-collector` for local RELP validation. This document is the single planning, status, and validation reference for the agent docs. The architecture details live in [`ARCHITECTURE.md`](ARCHITECTURE.md).

## Current implementation snapshot

Implemented foundation:

- .NET 10 solution split across Domain, Application, Profiles, KQL, Outputs, Forwarder, Daemon, CLI, input adapters, and Buffer projects.
- `DeltaZulu.Agent.Core` compatibility type-forwarding shim for older references.
- `System.Reactive` based input/runtime flow without custom observable infrastructure.
- Resource-profile YAML model, loader, validation, WMI host conditions, and KQL execution seam using `Microsoft.Rx.Kql`.
- NDJSON file/console sinks and compact table output for the exploration CLI.
- Syslog TCP/file input with lightweight parsing.
- CSV file input.
- Auditd parser and assembler with malformed-line handling, EOE/PROCTITLE completion, hex PATH decoding, and array handling for repeated record types.
- Windows Event Log, EVTX, ETL, and ETW inputs using the Tx.Windows approach.
- Windows Event Log named XML `EventData` extraction as nested payload fields and top-level convenience fields.
- Sample Linux, Windows Event Log, auditd, and ETW profiles.
- Shared application runtime that binds inputs, optional profiles, and outputs.
- Split hosts: `dzagentctl` for exploration, `dzagentd` for forwarder-only daemon operation, and `dzdemo-collector` for local RELP validation.
- YAML daemon configuration in `config/dzagentd.yaml` for sources, buffer, RELP endpoints/TLS policy, and diagnostics.
- `DeltaZulu.Buffer` durable chunk storage, checksums, atomic file transitions, retry, backpressure, recovery, metrics, and dead-letter support.
- RELP-neutral delivery records, batches, ACKs, transport port, buffered forwarder sink, RELP.Net adapter, ordered endpoint failover groundwork, and forwarder health snapshots.
- Stable `DeliveryId` per delivery envelope for at-least-once server deduplication.
- Metadata fallback injection so user KQL projections cannot accidentally remove delivery identity.
- Host-neutral tests for domain/application behavior, profile loading, KQL seams, syslog, auditd, CSV, NDJSON, forwarder behavior, observability, and buffer behavior.

Not implemented yet:

- Production service lifecycle integration for Windows Service Control Manager and systemd.
- Installer/package guidance.
- Fully validated RELP/TLS certificate callback wiring against production receiver builds.
- Profile hot reload.
- Source checkpoint advancement tied explicitly to durable enqueue.
- Journald input.
- Optional typed resource-local enrichment providers.
- Host-gated integration coverage for live auditd, Windows Event Log, EVTX, ETL, ETW, and future journald behavior.

## Completed forwarder-first outcomes

The old forwarder-first plan is complete and no longer maintained as a separate document. Its implemented outcomes are:

- RELP-neutral `DeliveryRecord`, `DeliveryBatch`, `DeliveryAck`, and transport boundary.
- Buffered forwarder path using `DeltaZulu.Buffer` as the durability/backpressure layer.
- RELP.Net-backed transport adapter hidden behind the transport boundary.
- Separate `dzdemo-collector` executable for local validation.
- Stable delivery IDs and metadata preservation across KQL projections.
- Forwarder health observations.
- Split `dzagentctl` exploration CLI and `dzagentd` daemon host.

## P0: Stabilize daemon forwarding

- Keep `dzagentd` focused on long-running forwarding only: configured sources, optional profiles, durable enqueue, RELP dispatch, and diagnostics.
- Exercise daemon smoke tests against `dzdemo-collector` for successful send, retry, permanent failure, dead-letter, and restart-recovery scenarios.
- Preserve delivery metadata outside user-controlled KQL projections and verify forwarded records keep agent/source/profile identity.
- Tie any future source checkpoint advancement to durable enqueue rather than network ACK.
- Keep operator examples aligned with `config/dzagentd.yaml` and documented cleanup expectations for local buffer directories.

## P0: Validation and compatibility

- Re-run restore, build, and tests whenever SDK versions, dependencies, Windows input adapters, KQL behavior, daemon configuration, or forwarder transport behavior change.
- Keep host-neutral unit and fixture tests fast and deterministic.
- Validate Windows Event Log, EVTX, ETL, and ETW behavior on Windows hosts when changing Windows adapters or profiles.
- Keep monitoring `Microsoft.Rx.Kql`, `Tx.Windows`, and `RELP.Net` compatibility with .NET 10.
- Add targeted golden fixtures only when they protect a real regression or newly supported source behavior.

### Project-by-project test focus

| Project | Current test focus | High-ROI scenarios | Deferred coverage |
| --- | --- | --- | --- |
| `DeltaZulu.Agent.Domain` / `DeltaZulu.Agent.Core` | Domain records, dictionary helpers, and compatibility type forwarding. | Dictionary coercion, metadata preservation, delivery identity, serialization helpers, and type-forwarding compatibility. | Additional compatibility tests only when older consumers require them. |
| `DeltaZulu.Agent.Inputs.Auditd` | Parser and assembler business logic using literal audit lines. | Audit line prefix validation, scalar coercion, hex argument decoding, record grouping by audit ID, ordered `ARGV`, multi-record fields such as `PATH`, and one-shot flush behavior. | Reading `/var/log/audit/audit.log`, tailing files, or interacting with auditd. |
| `DeltaZulu.Agent.Inputs.Syslog` | Dependency-free parser behavior using literal RFC 3164, RFC 5424, and unstructured messages. | Priority decoding, hostname/process extraction, source address preservation, key/value extraction, and raw-message preservation. | TCP listener behavior, journald, syslog daemon configuration, and socket-level error handling. |
| `DeltaZulu.Agent.Inputs.Files` | CSV row-to-event behavior using temporary files and culture-invariant parsing. | File roundtrip behavior, type coercion, malformed row diagnostics, and long-running file scenarios if added. | Filesystem watcher behavior. |
| `DeltaZulu.Agent.Inputs.Windows` | Host-neutral mapping helpers, named EventData extraction, and nested field exposure. | More XML mapping variants and optional provider fixtures. | ETW sessions, live Windows Event Log subscriptions, and EVTX/ETL host dependencies. |
| `DeltaZulu.Agent.Kql` | In-memory profile execution, no-match/error behavior, metadata fallback injection, and nested field access. | Source-family-specific nested field probes as profiles mature. | Query engine internals and package behavior outside the repository boundary. |
| `DeltaZulu.Agent.Outputs.Ndjson` | Serializer and error-record business logic. | Property-name preservation, null omission, compact single-line JSON settings, and exception-to-error mapping. | Console/file sink I/O failures and file permission integration cases. |
| `DeltaZulu.Agent.Profiles` | Profile validation business rules and YAML loading. | Minimal valid profiles, unsupported languages/formats, required fields, preservation requirements, WMI host conditions, and source-aware exception messages. | Full YAML fixture loading across every bundled production profile. |
| `DeltaZulu.Buffer` | Durable chunking, state transitions, retry, backpressure, metrics, recovery, and integration flows. | Write-dispatch-ACK, flush-on-stop, permanent failure dead-lettering, record-too-large rejection, checksum/corruption behavior, and option defaults. | Filesystem and disk-pressure behavior beyond deterministic temp-directory tests. |

### Required local validation

Run validation from the repository root on a host with the .NET 10 SDK installed. The repository targets `net10.0` and includes Windows-only inputs that target `net10.0-windows`, so Linux/macOS validation should cover host-neutral projects while Windows validation should additionally cover Event Log, EVTX, ETL, and ETW behavior.

Confirm the SDK and initialize the RELP.Net submodule when needed:

```bash
dotnet --list-sdks
git submodule update --init --recursive external/RELP.Net
```

Run host-neutral validation on any .NET 10-capable host:

```bash
dotnet restore DeltaZulu.Agent.slnx
dotnet build DeltaZulu.Agent.slnx --no-restore
dotnet test tests/DeltaZulu.Agent.Tests/DeltaZulu.Agent.Tests.csproj --no-build
dotnet test tests/DeltaZulu.Buffer.Tests/DeltaZulu.Buffer.Tests.csproj --no-build
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

### Forwarder and demo collector smoke test

Use two terminals on a .NET 10-capable host:

```bash
# terminal 1
dotnet run --project src/DeltaZulu.Demo.Collector -- --address 127.0.0.1 --port 6514

# terminal 2
printf '<34>1 2026-06-23T12:00:00Z host app 123 ID47 - test message\n' > /tmp/dzagent-smoke.log
cp config/dzagentd.yaml /tmp/dzagentd-smoke.yaml
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

The demo collector is only a local validation receiver. It is not a production collector, daemon, SIEM, or syslog daemon replacement.

## P1: Production RELP and TLS hardening

- Continue hardening the RELP.Net adapter behind the RELP-neutral transport boundary.
- Validate plain RELP and RELP/TLS wire behavior against production rsyslog and syslog-ng builds.
- Wire configured certificate policy into server-certificate validation when supported by the underlying RELP.Net surface.
- Add certificate-expiry diagnostics and clearer TLS failure reporting.
- Refine endpoint selection and transient/permanent failure classification while leaving durable retry scheduling in `DeltaZulu.Buffer`.
- Keep `docs/RELP_RECEIVER_SETUP.md` aligned with validated receiver behavior.

## P1: Operations

- Add service lifecycle integration for Windows Service Control Manager and systemd.
- Add installer/package guidance after daemon lifecycle behavior is stable.
- Add profile/config validation commands or startup diagnostics suitable for operators.
- Add profile hot reload after source checkpoint and forwarding semantics are clear.
- Expand health output into the eventual operational telemetry contract: written, sent, acknowledged, retried, dead-lettered, rejected, disk usage, and oldest buffered age.

## P2: Source and profile expansion

- Harden auditd assembler behavior further only where parser correctness requires it; keep LAUREL-level enrichment post-1.0.
- Add host-gated integration tests for auditd, Windows eventing, ETW/ETL/EVTX, and future journald support.
- Add journald input.
- Validate nested KQL access for auditd and other non-Windows source families when those profiles resume.
- Add typed resource-local enrichment providers after version 1.0 is stable and only where they do not perform server-canonical normalization.

## Architecture discipline

- Keep `DeltaZulu.Buffer` as the authoritative durability and backpressure layer.
- Keep RELP.Net details behind the forwarder transport adapter.
- Keep `dzagentctl` and `dzagentd` responsibilities separate.
- Keep input adapters source-native and avoid broad architecture-only reshuffles.
- Preserve original resource field names and defer semantic normalization to the server.

## Permanently out of scope

- DuckDB.
- SQL window engines.
- Edge-side server-canonical normalization.
- A built-in production syslog daemon replacement.
