# DeltaZulu Agent

DeltaZulu.Agent is a resource-native .NET 10 collection and forwarding agent. It filters source-native event fields with YAML profiles, writes NDJSON for exploration, and currently forwards records mapped into DeltaZulu.Forward's `ForwardLogBatch` contract through the pipeline-owned FORWARDER compatibility path backed by `DeltaZulu.DurableBuffer`. The target daemon uses LocalStream as its primitive in-agent stream substrate and DeltaZulu.Forward, a RELP-derived but non-wire-compatible protocol owned by Pipeline, as its transport (see [ADR 0011](docs/adr/0011-deltazulu-forward-transport.md)); the direct DurableBuffer forwarding path is transitional.

This package is not a SIEM, server-side normalization engine, or production syslog daemon replacement. It includes `dzagentctl` as the local agent controller, a separate `dzagentd` daemon host for service deployment, and a `dzagentd` collector configuration for local FORWARDER validation.


## Command line tool

DeltaZulu includes the `dzagentctl` controller in `src/DeltaZulu.Agent.Cli`.
It exposes controller workflows only: local KQL editing over built-in resource schemas, agent metrics views, KQL tailing for known file-backed resources, and daemon lifecycle commands.

```text
Usage:
  dzagentctl --tui [--profiles <profiles-dir>]
  dzagentctl --metrics [--sqlite <path>]
  dzagentctl --tail "<query>" <path>
  dzagentctl start|stop|restart|status|reload [-v] [--service <name>]

Controller modes:
  --tui                    Open the Terminal.Gui-backed local KQL editor TUI with built-in local resource schemas.
                           Use :schemas inside the TUI to discover queryable local tables.
  --metrics                Open the Terminal.Gui-backed agent metrics TUI from the daemon SQLite state (agent metrics only, not system htop).
  --tail "<query>" <path>  Open the Terminal.Gui-backed tail preflight and query file-backed resources with KQL; unknown formats fall back to a Lines table with a single line column.
  start/stop/restart/status/reload
                           Control the dzagentd service, systemctl-style.
```

Examples:

The controller implementation roadmap is tracked in [`docs/DZAGENTCTL_CONTROLLER_ROADMAP.md`](docs/DZAGENTCTL_CONTROLLER_ROADMAP.md).

```bash
dzagentctl --tui
# inside --tui: Processes\n| project name, memMB=workingSet/1024/1024\n| order by memMB desc\n| take 1
dzagentctl --metrics [--sqlite <path>]
dzagentctl --tail "Syslog | where Severity == 'error'" /var/log/syslog
dzagentctl status -v
```



## Agent daemon

## Daemon operating modes

`dzagentd` runs the forwarding side of the local FORWARDER smoke test: it discovers enabled resource profiles from `profilesPath`, reads each profile's resource input, applies the profile KQL filter, maps records into DeltaZulu.Forward's `ForwardLogBatch` contract, and sends them over the configured FORWARDER transport. Run the same `dzagentd` executable with `config/dzcollector.yaml` for the receiving side of local validation.

The current coordination contract is configuration files plus process supervision. The buffer is the durable handoff and retry state for FORWARDER output, so named pipes, a local database, or other IPC are not required for the initial split. Add IPC only when live reload or command/control operations require runtime mutation without restarting the daemon.

```bash
# Forwarding instance
dzagentd --config config/dzagent.yaml

# Receiving side for local validation
dzagentd --config config/dzcollector.yaml
```

`src/DeltaZulu.Agent.Daemon` builds the `dzagentd` host. Its production role is forwarding; use `config/dzcollector.yaml` for local receiver validation and controlled lab receiver tests. It is shaped as a long-running .NET Generic Host so the same binary can run in a console during development, as a plain Linux process in containers or non-systemd environments, and under Windows Service Control Manager on Windows or systemd on Linux when those service managers are present.

```bash
dzagentd --config config/dzagent.yaml
```

The daemon configuration points at a resource profile directory and owns only pipeline transport/runtime settings. Enabled profiles under `profilesPath` define the input resource (`resource.family`, `resource.channel`, `resource.session`, `resource.provider`) and the KQL filter, so daemon YAML no longer duplicates a separate `sources` list. Forwarding is explicitly configured as `pipeline.output.encoding: forward` over `pipeline.output.transport: forwarder`.

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
    encoding: forward
    transport: forwarder
buffer:
  path: ./buffer/agentd
forwarder:
  useTls: true
  endpoints:
    - host: ingest.example.com
      port: 443
```

### Single-port 443 client/server egress

Use TCP/443 as the only agent egress port for production client/server communication. Configure FORWARDER with TLS enabled and set every `forwarder.endpoints[].port` value to `443`; the forwarder already treats the endpoint list as failover targets, so all configured FORWARDER destinations should advertise the same firewall-approved port. Keep certificate validation enabled with system trust or thumbprint pinning, and use `forwarder.tls.clientCertificatePath` when the server requires mutual TLS.

During development, set `forwarder.tls.clientCertificateEnabled: false` to keep certificate paths in configuration without presenting an mTLS client certificate or requiring the configured file to exist. Leave it enabled for production mTLS.

If FORWARDER-over-TLS and future C2/control-plane traffic must share the same public address and port, terminate both protocols behind a TLS-aware edge proxy on 443 and route by SNI or ALPN rather than opening additional listener ports. For example, publish `forwarder-ingest.example.com:443` for FORWARDER-over-TLS and `agent-api.example.com:443` for C2/HTTPS, both resolving to the same edge. The edge can then forward FORWARDER traffic to the collector and C2 traffic to the control-plane service on private backend ports. Do not multiplex cleartext protocols on 443; keep TLS mandatory at the edge and restrict outbound firewall rules to destination TCP/443.

For local FORWARDER validation, the daemon collector configuration defaults to 2514 to avoid both privileged-port requirements and common Windows excluded ranges that can reserve 6514; production deployments should override the collector or edge listener to 443.

## Daemon collector mode

For local forwarder validation, run `dzagentd` with `config/dzcollector.yaml`. It is a standard runtime pipeline instance with a `ForwardLogBatch` FORWARDER input, pass-through filtering, and configurable console/file NDJSON output; acknowledgements are handled by the shared FORWARDER input adapter. Set `pipeline.output.prettyPrint: true` when you want each received JSON object expanded across multiple indented lines for readability.

```bash
dzagentd --config config/dzcollector.yaml
```

Visual Studio users can select the shared `Agent + Collector` solution launch profile to start both daemon instances together under the debugger. The profile launches `Run as Collector` first, then `Run as Agent`, using the two daemon launch profiles in `src/DeltaZulu.Agent.Daemon/Properties/launchSettings.json`.

Local controller workflows:

```bash
# Inspect local process memory from the KQL editor TUI.
dzagentctl --tui
# inside --tui: Processes\n| project name, memMB=workingSet/1024/1024\n| order by memMB desc\n| take 1

# Query a known file-backed resource while tailing it.
dzagentctl --tail "Syslog | where Severity == 'error'" /var/log/syslog

# Check daemon lifecycle state through the controller.
dzagentctl status -v
```

`dzagentctl` is the agent controller, not a standalone collection demo host. Use `--tui` for local schema-backed exploration, `--tail` for file-backed KQL filtering, `--metrics` for agent monitoring views, and `start|stop|restart|status|reload` for daemon lifecycle control. The `dzagentd` daemon remains responsible for production collection and forwarding from configured resource profiles.

Profiles may include an optional host condition; both `dzagentctl` and `dzagentd` honor it when deciding whether a profile should run on the current host. The first supported condition type is `wmi`, which runs a WQL query and enables the profile only when the query returns at least one row. Set `condition.mandatory: false` when an unavailable WMI provider should skip the profile with a warning instead of failing startup. This is useful for Windows resource profiles that should only run on a specific server role, such as domain controllers:

```yaml
condition:
  type: wmi
  query: select * from Win32_OperatingSystem where ProductType=2
  mandatory: false
```

The `schemas` command always lists built-in input resource schemas, so it works before any profile files exist. If the `profiles` directory (or another directory passed on the command line) exists, profile schemas are appended to the same output. Pass optional `table` or `json` format when you need to discover the resource ids, input tables, and schema strings available on the host while deciding which profile files still need to be created or tuned.

## Current implementation status

- **Architecture migration status (2026-07-19):** Phases 0-1 are complete and
  Phase 2 is active. Current work introduces text and structured input
  contracts with metadata-preserving adapters; it does not yet add Parse
  parsing, LocalStream runtime behavior, the target binary/Avro
  DeltaZulu.Forward state machine, or the execution-plan daemon.
- `DeltaZulu.Pipeline` is the single reusable, multi-targeted pipeline assembly. Its internal `Core`, `Inputs`, `Parsing`, `Assembly`, `Streaming`, `Dispatch`, `Enrichment`, `Outputs`, and `Tunnel` boundaries are folders and namespaces, not separate projects.
- The target has distinct text and structured input contracts. Inputs acquire, frame, decode, or map; `DeltaZulu.Parse` (renamed from `DeltaZulu.Normalize`, [ADR 0013](docs/adr/0013-parse-naming.md)) will be the only plaintext structural parser, while deterministic/native structured inputs bypass it.
- The current daemon remains transitional: it still executes profile-centric pipelines, uses direct `DeltaZulu.DurableBuffer` forwarding, and serializes legacy concurrent output with `ChannelOutputMultiplexer`.
- The target daemon uses one LocalStream host with `agent.parsed` (materialization-to-filter) and `agent.output` (filter-to-forwarder). LocalStream is a primitive in-agent stream substrate; it is not a DurableBuffer wrapper. Logical topics are parsed-envelope values; they are not physical streams.
- DeltaZulu.Forward ([ADR 0011](docs/adr/0011-deltazulu-forward-transport.md)) is the target transport. The FORWARDER compatibility path now sends `ForwardLogBatch` typed-batch frames, encoded by `ForwardLogBatchCodec` in the DeltaZulu.Forward package rather than by Pipeline. Output commits occur only after a forwarding acknowledgement either way.
- `parse.query` will be an optional restricted Parse-rule contract; `filter.query` remains Rx.Kql-owned. Profiles do not configure streams, offsets, partitions, parser generations, or multiplexer behavior.
- Unrecognized plaintext is preserved and coverage distinguishes admission rejection, parser no-match, filter no-candidate, filter no-match, and operational errors.
- Agent output preserves source-native field names; server-side DeltaZulu components perform canonical semantic normalization. The type-contract catalog ([ADR 0010](docs/adr/0010-type-catalog-avro-arrow-and-ndjson-edge-dialect.md)) governs representation, not this semantic layer.

## Project layout

```text
src/
  DeltaZulu.Pipeline/
    Core/  Inputs/  Parsing/  Assembly/  Streaming/  Dispatch/
    Enrichment/  Outputs/  Tunnel/
  DeltaZulu.Agent.Runtime/
  DeltaZulu.Agent.Filter/
  DeltaZulu.Agent.Daemon/
  DeltaZulu.Agent.Cli/
  DeltaZulu.Agent.ProfileWorkbench/
tests/
  DeltaZulu.Agent.Tests/
docs/
  adr/
```

## Package sources

DeltaZulu package dependencies are resolved from NuGet package sources rather
than git submodules. `Directory.Packages.props` pins package versions, including:

- `DeltaZulu.DurableBuffer` for the current transitional forwarding spool.
- `DeltaZulu.Forward` for shared forwarder protocol contracts.
- `DeltaZulu.Parse` for the Parse assembly boundary.
- `DeltaZulu.LocalStream` for the LocalStream package boundary.

`NuGet.config` maps `DeltaZulu.*` packages to a package source named
`deltazulu-github`; the build environment must register that source, while
general third-party packages resolve from `nuget.org`. LocalStream is consumed
as a package reference; do not reintroduce an in-repository project/submodule
placeholder.

## Documentation

- [Architecture](docs/ARCHITECTURE.md) is the authoritative target topology and dependency boundary.
- [Roadmap](docs/ROADMAP.md) tracks the staged migration and current transitional baseline.
- [Architecture ADRs](docs/adr/) record the durable assembly, input, parsing, streaming, transport, and coverage decisions, including [DeltaZulu.Forward transport naming](docs/adr/0011-deltazulu-forward-transport.md) and [Proton ingestion via an intermediate protocol](docs/adr/0012-proton-ingestion-intermediate-protocol.md).
- [RELP receiver setup](docs/RELP_RECEIVER_SETUP.md) is historical guidance for the older literal RELP validation path; the checked-in sample daemon configs now use the FORWARDER validation path.

## License

DeltaZulu is licensed under AGPL-3.0 with the additional permission described in `LICENSE-EXCEPTION-KUSTO.md`.

DeltaZulu may use Microsoft-published Kusto Query Language components, including `Microsoft.Azure.Kusto.Language`, as unmodified third-party dependencies for KQL parsing, semantic analysis, schema-aware authoring, and validation. Those components are not part of the DeltaZulu covered work and remain subject to their own applicable license terms, including Apache License 2.0 where applicable and any Microsoft package license terms that apply to the specific distributed artifact.

DeltaZulu is not Azure Data Explorer and does not include an Azure Data Explorer connector under this exception.
