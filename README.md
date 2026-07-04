  # DeltaZulu Agent

DeltaZulu.Agent is a resource-native .NET 10 collection and forwarding agent. It filters and selects source-native event fields with KQL-style YAML profiles, writes NDJSON for exploration, and forwards durable delivery records as MessagePack `DeliveryBatch` payloads through `DeltaZulu.DurableBuffer` and a RELP.Net-backed transport adapter.

This package is not a SIEM, server-side normalization engine, or production syslog daemon replacement. It includes a thin `dzagentctl` CLI for local exploration, a separate `dzagentd` daemon host for service deployment, and a `dzagentd` collector configuration for local RELP validation.


## Command line tool

DeltaZulu now includes a small `dzagentctl`-style console host in `src/DeltaZulu.Agent.Cli`.
It is intentionally thin: the executable wires the existing input libraries, KQL profile executor, pipeline helper, and NDJSON output sinks together so local event exploration has the same resource-profile behavior as daemon hosts.

```text
Usage:
  dzagentctl <input> [<arg>] [json [<file.ndjson>]] [--profile <profile.yaml>]
  dzagentctl <input> [<arg>] [json [<file.ndjson>]] --kql <query> [--table <name>] [--schema <columns>]
  dzagentctl schemas [<profiles-dir>] [table|json]

Inputs:
  syslog <file>             Tail a local syslog-style file for new events.
  syslogserver [options]    Listen for syslog lines over TCP (default 0.0.0.0:514).
  fifo <path>               Create/read a Linux FIFO for syslog-style log lines.
  csv <file.csv>            Process a CSV file and then exit.
  auditd <file>             Process an auditd log file and then exit.
  eventlog <logname>        Listen for new Windows Event Log events (Windows build).
  evtx <file.evtx>          Process an EVTX file (Windows build).
  etl <file.etl>            Process an ETL trace file (Windows build).
  etw <session>             Listen to a real-time ETW session (Windows build).

Outputs:
  json [file.ndjson]        Write DeltaZulu NDJSON to stdout or append to a file (default).


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
dzagentctl syslog /var/log/auth.log --profile profiles/linux/syslog/sshd.yaml
dzagentctl csv events.csv json out.ndjson --kql "Source | where RawMessage has 'sudo'"
dzagentctl syslogserver --address 127.0.0.1 --port 5514
dzagentctl fifo /run/deltazulu/logs.fifo json fifo.ndjson --kql "Source | project ReceivedAt, RawMessage, Message"
dzagentd --config config/dzcollector.yaml
```



## Agent daemon

## Daemon operating modes

`dzagentd` runs the forwarding side of the local RELP smoke test: it discovers enabled resource profiles from `profilesPath`, reads each profile's resource input, applies the profile KQL filter, encodes delivery batches with MessagePack, and sends them over RELP. Run the same `dzagentd` executable with `config/dzcollector.yaml` for the receiving side of local validation.

The current coordination contract is configuration files plus process supervision. The buffer is the durable handoff and retry state for RELP output, so named pipes, a local database, or other IPC are not required for the initial split. Add IPC only when live reload or command/control operations require runtime mutation without restarting the daemon.

```bash
# Forwarding instance
dzagentd --config config/dzagent.yaml

# Receiving side for local validation
dzagentd --config config/dzcollector.yaml
```

`src/DeltaZulu.Agent.Daemon` builds the `dzagentd` host. Unlike the development CLI, this executable is intentionally non-exploratory: it has no inline query, schema listing, table output, JSON export, or other ad-hoc exploration commands. Its production role is forwarding; use `config/dzcollector.yaml` for local receiver validation and controlled lab receiver tests. It is shaped as a long-running .NET Generic Host so the same binary can run in a console during development, as a plain Linux process in containers or non-systemd environments, and under Windows Service Control Manager on Windows or systemd on Linux when those service managers are present.

```bash
dzagentd --config config/dzagent.yaml
```

The daemon configuration points at a resource profile directory and owns only pipeline transport/runtime settings. Enabled profiles under `profilesPath` define the input resource (`resource.family`, `resource.channel`, `resource.session`, `resource.provider`) and the KQL filter/projection, so daemon YAML no longer duplicates a separate `sources` list. Forwarding is explicitly configured as `pipeline.output.encoding: messagepack` over `pipeline.output.transport: relp`.

```yaml
id: local-agent-daemon
profilesPath: profiles
pipeline:
  input:
    mode: profiles
  filter:
    mode: profiles
  output:
    mode: forward
    encoding: messagepack
    transport: relp
buffer:
  path: ./buffer/agentd
relp:
  useTls: true
  endpoints:
    - host: ingest.example.com
      port: 443
```

### Single-port 443 client/server egress

Use TCP/443 as the only agent egress port for production client/server communication. Configure RELP with TLS enabled and set every `relp.endpoints[].port` value to `443`; the forwarder already treats the endpoint list as failover targets, so all configured RELP destinations should advertise the same firewall-approved port. Keep certificate validation enabled with system trust or thumbprint pinning, and use `relp.tls.clientCertificatePath` when the server requires mutual TLS.

During development, set `relp.tls.clientCertificateEnabled: false` to keep certificate paths in configuration without presenting an mTLS client certificate or requiring the configured file to exist. Leave it enabled for production mTLS.

If RELP-over-TLS and future C2/control-plane traffic must share the same public address and port, terminate both protocols behind a TLS-aware edge proxy on 443 and route by SNI or ALPN rather than opening additional listener ports. For example, publish `relp-ingest.example.com:443` for RELP-over-TLS and `agent-api.example.com:443` for C2/HTTPS, both resolving to the same edge. The edge can then forward RELP traffic to the RELP receiver and C2 traffic to the control-plane service on private backend ports. Do not multiplex cleartext protocols on 443; keep TLS mandatory at the edge and restrict outbound firewall rules to destination TCP/443.

For local RELP validation, the daemon collector configuration defaults to 2514 to avoid both privileged-port requirements and common Windows excluded ranges that can reserve 6514; production deployments should override the collector or edge listener to 443.

## Daemon collector mode

For local forwarder validation, run `dzagentd` with `config/dzcollector.yaml`. It is a standard runtime pipeline instance with a MessagePack RELP input, pass-through filtering, and configurable console/file NDJSON output; RELP acknowledgements are handled by the shared RELP input adapter. Set `pipeline.output.prettyPrint: true` when you want each received JSON object expanded across multiple indented lines for readability.

```bash
dzagentd --config config/dzcollector.yaml
```

Visual Studio users can select the shared `Agent + Collector` solution launch profile to start both daemon instances together under the debugger. The profile launches `Run as Collector` first, then `Run as Agent`, using the two daemon launch profiles in `src/DeltaZulu.Agent.Daemon/Properties/launchSettings.json`.

Windows process creation examples:

```bash
# Sysmon process creation, Event ID 1, to console NDJSON.
dzagentctl eventlog sysmon --kql "Source | where EventId == 1 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"

# Windows Security process creation, Event ID 4688, to console NDJSON.
dzagentctl eventlog Security --kql "Source | where EventId == 4688 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"

# Windows Security process creation, Event ID 4688, to a file sink.
dzagentctl eventlog Security json security-4688.ndjson --kql "Source | where EventId == 4688 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"
```

`eventlog sysmon` expands to `Microsoft-Windows-Sysmon/Operational`. If that log is not present, install Sysmon or choose another available Windows Event Log channel before querying Event ID 1.
The CLI validates requested Windows Event Log resources before starting KQL. Profiles default to `mandatory: true`, which keeps missing resources as `error:` conditions; set `mandatory: false` on optional profiles to log a `warning:` for that profile and continue with the remaining profiles instead of surfacing a Reactive/KQL exception stack.

Without a profile, source events pass through unchanged into the standard DeltaZulu NDJSON envelope.
With `--profile`, the CLI loads a DeltaZulu YAML resource profile and executes its KQL filter/select query through `DeltaZulu.Agent.Filter`.
With `--kql`, the CLI wraps the inline query in a temporary local resource profile so you can query an input in real time without creating a YAML file first. Output still defaults to console NDJSON, or can be routed to a file sink with `json out.ndjson`.
CLI options such as `--kql` can appear before or after the input command; for example, `dzagentctl --kql "Source | where EventId == 1" eventlog Microsoft-Windows-Sysmon/Operational` is equivalent to placing `--kql` after the `eventlog` arguments.

Profiles may include an optional host condition; both `dzagentctl` and `dzagentd` honor it when deciding whether a profile should run on the current host. The first supported condition type is `wmi`, which runs a WQL query and enables the profile only when the query returns at least one row. Set `condition.mandatory: false` when an unavailable WMI provider should skip the profile with a warning instead of failing startup. This is useful for Windows resource profiles that should only run on a specific server role, such as domain controllers:

```yaml
condition:
  type: wmi
  query: select * from Win32_OperatingSystem where ProductType=2
  mandatory: false
```

The `schemas` command always lists built-in input resource schemas, so it works before any profile files exist. If the `profiles` directory (or another directory passed on the command line) exists, profile schemas are appended to the same output. Pass optional `table` or `json` format when you need to discover the resource ids, input tables, and schema strings available on the host while deciding which profile files still need to be created or tuned.

## Current implementation status

- `DeltaZulu.Agent.Pipeline` contains ETL pipeline contracts, source events, resource outputs, profile models/loaders/validators, delivery envelopes, observation records, RELP frame helpers, NDJSON options, MessagePack payload wrappers, completion tracking, and output multiplexing.
- `DeltaZulu.Agent.Runtime` contains daemon/CLI orchestration primitives such as the shared runtime and profile binding used by both hosts. It also contains a Windows-only, read-only ETW integrity monitor that baselines `ntdll!EtwEventWrite` and `ntdll!NtTraceEvent` inside the agent process and reports agent-health findings when common user-mode ETW bypass patches alter those prologues.
- `dzagentctl` remains an exploration CLI for schemas, inline KQL, profile testing, and NDJSON output.
- `dzagentd` is the YAML-configured daemon host. Its production role is forwarding, and its collector-style configuration is reserved for local validation or controlled lab tests.
- `DeltaZulu.DurableBuffer` is the durable queue and backpressure layer before RELP dispatch.
- `DeltaZulu.Agent.Outputs` owns NDJSON sinks plus RELP buffered forwarding, RELP-neutral transport contracts, DeltaZulu.Relp transport, endpoint failover groundwork, TLS policy options, and health snapshots. Optional MessagePack output support is isolated under `DeltaZulu.Agent.Outputs/MessagePack`.
- Input families include syslog files, TCP syslog, Linux FIFO paths, CSV, auditd, Windows Event Log, EVTX, ETL, and ETW. Optional MessagePack input support is isolated under `DeltaZulu.Agent.Inputs/MessagePack`.
- Windows Event Log named `EventData` values are available both as nested payload fields and top-level convenience fields for profiles.
- Agent output preserves source-native field names; server-side DeltaZulu components perform semantic normalization.
- ETW integrity monitoring is intentionally scoped to agent self-protection diagnostics: it checks only the current process's `ntdll` ETW prologues, does not unhook or repair memory, and should emit `AgentIntegrityFinding`-style internal security/health events rather than normal endpoint telemetry.
- Detection verdicts, DuckDB, SQL window engines, and platform-owned canonical normalization remain out of scope for the agent; deterministic post-filter enrichment lives in DeltaZulu.Pipeline.Enrichment.

## Project layout

```text
src/
  DeltaZulu.Agent.Pipeline/
    Abstractions/
    Delivery/
    Events/
    MessagePack/
    Ndjson/
    Observability/
    Profiles/
    Relp/
  DeltaZulu.Agent.Runtime/
    Security/EtwIntegrity/
  DeltaZulu.Agent.Filter/
  DeltaZulu.Agent.Outputs/
    Ndjson/
    Relp/
    MessagePack/
  DeltaZulu.Agent.Daemon/
  DeltaZulu.Agent.Inputs/
    Relp/
    Syslog/
    MessagePack/
    Files/
    Auditd/
    Windows/
  DeltaZulu.DurableBuffer/  (git submodule)
tests/
  DeltaZulu.Agent.Tests/
  DeltaZulu.DurableBuffer.Tests/
profiles/
  linux/
  windows/
docs/
external/
  DeltaZulu.Relp/  (git submodule)
```

## Git submodules

This repository tracks two direct git submodules:

- `external/DeltaZulu.Relp` for the RELP protocol implementation used by the agent transport adapter.
- `external/DeltaZulu.DurableBuffer` for durable queueing, retry, backpressure, and dead-letter behavior.

Because Git does not initialize newly added submodules during a normal `git fetch` or `git pull`, run submodule initialization after pulling changes if either external directory appears empty:

```bash
git submodule update --init --recursive external/DeltaZulu.Relp external/DeltaZulu.DurableBuffer
```

For a fresh clone, clone with submodules enabled:

```bash
git clone --recurse-submodules <repo-url>
```

To update the submodules to the latest commits from their tracked branches, use:

```bash
git submodule update --remote external/DeltaZulu.Relp external/DeltaZulu.DurableBuffer
```

## Documentation

- [Architecture](docs/ARCHITECTURE.md) describes the current host split, project boundaries, data flow, delivery envelopes, and normalization boundary.
- [Roadmap](docs/ROADMAP.md) tracks implementation status, production hardening work, validation commands, and forwarder smoke testing.
- [RELP receiver setup](docs/RELP_RECEIVER_SETUP.md) captures local and production receiver notes.
