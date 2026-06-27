# DeltaZulu Agent

DeltaZulu.Agent is a resource-native .NET 10 collection and forwarding agent. It filters and selects source-native event fields with KQL-style YAML profiles, writes NDJSON for exploration, and forwards durable delivery records through `DeltaZulu.Buffer` and a RELP.Net-backed transport adapter.

This package is not a SIEM, server-side normalization engine, or production syslog daemon replacement. It includes a thin `dzagentctl` CLI for local exploration, a separate `dzagentd` forwarder-only daemon host for service deployment, and a `dzdemo-collector` receiver for local RELP validation.


## Command line tool

DeltaZulu now includes a small `dzagentctl`-style console host in `src/DeltaZulu.Agent.Cli`.
It is intentionally thin: the executable wires the existing input libraries, KQL profile executor, pipeline helper, and NDJSON output sinks together so local event exploration has the same resource-profile behavior as daemon hosts.

```text
Usage:
  dzagentctl <input> [<arg>] [<output> [<arg>]] [--profile <profile.yaml>]
  dzagentctl <input> [<arg>] [<output> [<arg>]] --kql <query> [--table <name>] [--schema <columns>]
  dzagentctl schemas [<profiles-dir>] [table|json]

Inputs:
  syslog <file>             Tail a local syslog-style file for new events.
  syslogserver [options]    Listen for syslog lines over TCP (default 0.0.0.0:514).
  csv <file.csv>            Process a CSV file and then exit.
  auditd <file>             Process an auditd log file and then exit.
  eventlog <logname>        Listen for new Windows Event Log events (Windows build).
  evtx <file.evtx>          Process an EVTX file (Windows build).
  etl <file.etl>            Process an ETL trace file (Windows build).
  etw <session>             Listen to a real-time ETW session (Windows build).

Outputs:
  json [file.ndjson]        Write DeltaZulu NDJSON to stdout or append to a file (default).
  table                     Print a compact console table.


Options:
  --profile <profile.yaml>  Apply a DeltaZulu YAML resource profile containing KQL.
  --kql, -q, --query        Apply inline KQL to the real-time input stream.
  --table <name>            KQL table name for --kql (default Source).
  --schema <columns>        Resource schema text to associate with --kql.
  --resource-id <id>        Resource id to stamp on --kql output metadata.
  --address <ip>            syslogserver bind address.
  --port <port>             syslogserver TCP port.
```

Examples:

```bash
dzagentctl syslog /var/log/auth.log table --profile profiles/linux/syslog/sshd.yaml
dzagentctl csv events.csv json out.ndjson --kql "Source | where RawMessage has 'sudo'"
dzagentctl syslogserver --address 127.0.0.1 --port 5514
dzdemo-collector --address 127.0.0.1 --port 6514
```



## Agent daemon

`src/DeltaZulu.Agent.Daemon` builds the `dzagentd` host. Unlike the development CLI, this executable is intentionally forwarder-only: it has no inline query, schema listing, table output, JSON export, or other exploration commands. It is shaped as a long-running .NET Generic Host so the same binary can run in a console during development and later under Windows Service Control Manager or systemd.

```bash
dzagentd --config config/dzagentd.yaml
```

The daemon configuration owns live input families plus the existing durable buffer and RELP transport settings. Source events expose a KQL `source` column derived from the native source name, so Event Log, auditd, and ETW profiles select channels/providers in KQL (for example `EventLog | where source =~ "Security"` or `Etw | where source =~ "Microsoft-Windows-Kernel-Process"`) instead of adding ad-hoc daemon query flags. Each source may reference a checked-in YAML profile; omitted profiles pass source events through to the forwarder envelope.

```yaml
id: local-agent-daemon
sources:
  - id: local-syslog
    input: syslog
    target: /var/log/auth.log
    profile: profiles/linux/syslog/sshd.yaml
buffer:
  path: ./buffer/agentd
relp:
  endpoints:
    - host: 127.0.0.1
      port: 6514
```

## Demo collector

For local forwarder validation, use the separate `dzdemo-collector` executable from `src/DeltaZulu.Demo.Collector`.
It accepts RELP `syslog` frames, prints decoded DeltaZulu delivery batches, and
acknowledges them with RELP `rsp 200` responses.

```bash
dzdemo-collector --address 127.0.0.1 --port 6514
```

Windows process creation examples:

```bash
# Sysmon process creation, Event ID 1, to console NDJSON.
dzagentctl eventlog sysmon --kql "Source | where EventId == 1 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"

# Sysmon process creation, Event ID 1, to a compact console table.
dzagentctl eventlog sysmon table --kql "Source | where EventId == 1 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"

# Windows Security process creation, Event ID 4688, to console NDJSON.
dzagentctl eventlog Security --kql "Source | where EventId == 4688 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"

# Windows Security process creation, Event ID 4688, to a file sink.
dzagentctl eventlog Security json security-4688.ndjson --kql "Source | where EventId == 4688 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"
```

`eventlog sysmon` expands to `Microsoft-Windows-Sysmon/Operational`. If that log is not present, install Sysmon or choose another available Windows Event Log channel before querying Event ID 1.
The CLI validates requested Windows Event Log resources before starting KQL. Profiles default to `mandatory: true`, which keeps missing resources as `error:` conditions; set `mandatory: false` on optional profiles to log a `warning:` for that profile and continue with the remaining profiles instead of surfacing a Reactive/KQL exception stack.

Without a profile, source events pass through unchanged into the standard DeltaZulu NDJSON envelope.
With `--profile`, the CLI loads a DeltaZulu YAML resource profile and executes its KQL filter/select query through `DeltaZulu.Agent.Kql`.
With `--kql`, the CLI wraps the inline query in a temporary local resource profile so you can query an input in real time without creating a YAML file first. Output still defaults to console NDJSON, or can be routed to another sink with the existing output parameters such as `json out.ndjson` or `table`.
CLI options such as `--kql` can appear before or after the input command; for example, `dzagentctl --kql "Source | where EventId == 1" eventlog Microsoft-Windows-Sysmon/Operational` is equivalent to placing `--kql` after the `eventlog` arguments.

Profiles may include an optional host condition. The first supported condition type is `wmi`, which runs a WQL query and enables the profile only when the query returns at least one row. This is useful for Windows resource profiles that should only run on a specific server role, such as domain controllers:

```yaml
condition:
  type: wmi
  query: select * from Win32_OperatingSystem where ProductType=2
```

The `schemas` command always lists built-in input resource schemas, so it works before any profile files exist. If the `profiles` directory (or another directory passed on the command line) exists, profile schemas are appended to the same output. Pass optional `table` or `json` format when you need to discover the resource ids, input tables, and schema strings available on the host while deciding which profile files still need to be created or tuned.

## Current implementation status

- `DeltaZulu.Agent.Domain` contains source events, resource outputs, profile models, delivery envelopes, and observation records.
- `DeltaZulu.Agent.Application` contains the shared runtime, profile binding, pipeline orchestration, and output multiplexing used by both hosts.
- `dzagentctl` remains an exploration CLI for schemas, inline KQL, profile testing, NDJSON, and table output.
- `dzagentd` is the forwarder-only daemon host configured by `config/dzagentd.yaml`.
- `DeltaZulu.Buffer` is the durable queue and backpressure layer before RELP dispatch.
- `DeltaZulu.Agent.Forwarder` owns delivery serialization, buffered forwarding, RELP-neutral transport contracts, RELP.Net transport, endpoint failover groundwork, TLS policy options, and health snapshots.
- Input families include syslog, CSV, auditd, Windows Event Log, EVTX, ETL, and ETW.
- Windows Event Log named `EventData` values are available both as nested payload fields and top-level convenience fields for profiles.
- Agent output preserves source-native field names; server-side DeltaZulu components perform semantic normalization.
- Enrichment, DuckDB, SQL window engines, and edge-side canonical normalization remain out of scope for the agent.

## DeltaZulu.Buffer

A local durable buffering library that provides crash-safe, disk-backed buffering between the collector pipeline and network forwarder. The forwarder uses this buffer as the authoritative durability/backpressure layer before dispatching batches through the RELP.Net transport adapter by default. Features include binary chunk format with SHA-256 checksums, exponential backoff retry with jitter, backpressure control, dead-lettering, and atomic file-based state transitions.

See [docs/BUFFER_ARCHITECTURE.md](docs/BUFFER_ARCHITECTURE.md) for full design documentation.

## Project layout

```text
src/
  DeltaZulu.Agent.Domain/
  DeltaZulu.Agent.Application/
  DeltaZulu.Agent.Core/        (compatibility type-forwarding shim)
  DeltaZulu.Agent.Profiles/
  DeltaZulu.Agent.Kql/
  DeltaZulu.Agent.Outputs.Ndjson/
  DeltaZulu.Agent.Forwarder/
  DeltaZulu.Agent.Daemon/
  DeltaZulu.Demo.Collector/
  DeltaZulu.Agent.Inputs.Syslog/
  DeltaZulu.Agent.Inputs.Files/
  DeltaZulu.Agent.Inputs.Auditd/
  DeltaZulu.Agent.Inputs.Windows/
  DeltaZulu.Buffer/
tests/
  DeltaZulu.Agent.Tests/
  DeltaZulu.Buffer.Tests/
profiles/
  linux/
  windows/
docs/
external/
  RELP.Net/  (git submodule)
```

## Git submodules

This repository tracks RELP.Net as a Git submodule at `external/RELP.Net`.
Because Git does not initialize newly added submodules during a normal `git fetch`
or `git pull`, run the submodule initialization command after pulling this
change if `external/RELP.Net` appears as an empty directory:

```bash
git submodule update --init --recursive external/RELP.Net
```

For a fresh clone, clone with submodules enabled:

```bash
git clone --recurse-submodules <repo-url>
```

To update the RELP.Net submodule to the latest commit from its tracked branch,
use:

```bash
git submodule update --remote external/RELP.Net
```

## Documentation

- [Architecture](docs/ARCHITECTURE.md) describes the current host split, project boundaries, data flow, delivery envelopes, and normalization boundary.
- [Roadmap](docs/ROADMAP.md) tracks implementation status, production hardening work, validation commands, and forwarder smoke testing.
- [RELP receiver setup](docs/RELP_RECEIVER_SETUP.md) captures local and production receiver notes.
