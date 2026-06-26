# DeltaZulu Agent

Agent is inspired by RealTimeKql, but it is a new DeltaZulu implementation. It is a daemon-consumable .NET library for resource-native event filtering and field selection using KQL-style YAML profiles.

This package is not a daemon, installer, SIEM, or server-side normalization engine. It now includes a thin CLI host for local exploration; a future daemon can host the same libraries and wire them to local syslog, Windows Event Log, ETW, auditd plugin input, files, and output sinks.


## Command line tool

DeltaZulu now includes a small `dzagent`-style console host in `src/DeltaZulu.Agent.Cli`.
It is intentionally thin: the executable wires the existing input libraries, KQL profile executor, pipeline helper, and NDJSON output sinks together so local event exploration has the same resource-profile behavior as daemon hosts.

```text
Usage:
  dzagent <input> [<arg>] [<output> [<arg>]] [--profile <profile.yaml>]
  dzagent <input> [<arg>] [<output> [<arg>]] --kql <query> [--table <name>] [--schema <columns>]
  dzagent schemas [<profiles-dir>] [table|json]

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
  forwarder [buffer-dir]    Buffer filtered records locally and send them to a RELP collector.


Options:
  --profile <profile.yaml>  Apply a DeltaZulu YAML resource profile containing KQL.
  --kql, -q, --query        Apply inline KQL to the real-time input stream.
  --table <name>            KQL table name for --kql (default Source).
  --schema <columns>        Resource schema text to associate with --kql.
  --resource-id <id>        Resource id to stamp on --kql output metadata.
  --address <ip>            syslogserver bind address.
  --port <port>             syslogserver TCP port.
  --forwarder-host <host>    Forwarder target host for forwarder output (default 127.0.0.1).
  --forwarder-port <port>    RELP collector target port for forwarder output (default 6514).
  --forwarder-tls           Use TLS for RELP forwarder output.
  --forwarder-buffer <dir>   Buffer directory for forwarder output.
```

Examples:

```bash
dzagent syslog /var/log/auth.log table --profile profiles/linux/syslog/sshd.yaml
dzagent csv events.csv json out.ndjson --kql "Source | where RawMessage has 'sudo'"
dzagent syslogserver --address 127.0.0.1 --port 5514
dzdemo-collector --address 127.0.0.1 --port 6514
dzagent syslog /var/log/auth.log forwarder ./buffer --forwarder-host 127.0.0.1 --forwarder-port 6514
```

## Demo collector

The agent CLI does not run as a collector/server. For local forwarder validation,
use the separate `dzdemo-collector` executable from `src/DeltaZulu.Demo.Collector`.
It accepts RELP `syslog` frames, prints decoded DeltaZulu delivery batches, and
acknowledges them with RELP `rsp 200` responses.

```bash
dzdemo-collector --address 127.0.0.1 --port 6514
dzagent syslog /var/log/auth.log forwarder ./buffer --forwarder-host 127.0.0.1 --forwarder-port 6514
```

Windows process creation examples:

```bash
# Sysmon process creation, Event ID 1, to console NDJSON.
dzagent eventlog sysmon --kql "Source | where EventId == 1 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"

# Sysmon process creation, Event ID 1, to a compact console table.
dzagent eventlog sysmon table --kql "Source | where EventId == 1 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"

# Windows Security process creation, Event ID 4688, to console NDJSON.
dzagent eventlog Security --kql "Source | where EventId == 4688 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"

# Windows Security process creation, Event ID 4688, to a file sink.
dzagent eventlog Security json security-4688.ndjson --kql "Source | where EventId == 4688 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"
```

`eventlog sysmon` expands to `Microsoft-Windows-Sysmon/Operational`. If that log is not present, install Sysmon or choose another available Windows Event Log channel before querying Event ID 1.
The CLI validates requested Windows Event Log resources before starting KQL. Profiles default to `mandatory: true`, which keeps missing resources as `error:` conditions; set `mandatory: false` on optional profiles to log a `warning:` for that profile and continue with the remaining profiles instead of surfacing a Reactive/KQL exception stack.

Without a profile, source events pass through unchanged into the standard DeltaZulu NDJSON envelope.
With `--profile`, the CLI loads a DeltaZulu YAML resource profile and executes its KQL filter/select query through `DeltaZulu.Agent.Kql`.
With `--kql`, the CLI wraps the inline query in a temporary local resource profile so you can query an input in real time without creating a YAML file first. Output still defaults to console NDJSON, or can be routed to another sink with the existing output parameters such as `json out.ndjson` or `table`.
CLI options such as `--kql` can appear before or after the input command; for example, `dzagent --kql "Source | where EventId == 1" eventlog Microsoft-Windows-Sysmon/Operational` is equivalent to placing `--kql` after the `eventlog` arguments.

Profiles may include an optional host condition. The first supported condition type is `wmi`, which runs a WQL query and enables the profile only when the query returns at least one row. This is useful for Windows resource profiles that should only run on a specific server role, such as domain controllers:

```yaml
condition:
  type: wmi
  query: select * from Win32_OperatingSystem where ProductType=2
```

The `schemas` command always lists built-in input resource schemas, so it works before any profile files exist. If the `profiles` directory (or another directory passed on the command line) exists, profile schemas are appended to the same output. Pass optional `table` or `json` format when you need to discover the resource ids, input tables, and schema strings available on the host while deciding which profile files still need to be created or tuned.

## Current implementation goals

- Use `DeltaZulu.Agent.*` namespaces rather than the RealTimeKql namespace.
- Target .NET 10.
- Remove custom observable classes and use `System.Reactive` directly.
- Provide ETW, ETL, EVTX, and Windows Event Log capabilities inspired by the RealTimeKql input model.
- Replace the missing `Microsoft.Syslog` project dependency with a clear syslog input boundary and a lightweight parser.
- Add resource-profile YAML files that perform KQL filter/select operations.
- Emit structured NDJSON using `_metadata` and `event` envelopes.
- Preserve original resource field names.
- Push semantic normalization to the server.
- Keep enrichment in the roadmap, implemented later through typed resource-local state providers.
- Exclude DuckDB permanently.

## DeltaZulu.Buffer

A local durable buffering library that provides crash-safe, disk-backed buffering between the collector pipeline and network forwarder. The forwarder uses this buffer as the authoritative durability/backpressure layer before dispatching batches through the RELP.Net transport adapter by default. Features include binary chunk format with SHA-256 checksums, exponential backoff retry with jitter, backpressure control, dead-lettering, and atomic file-based state transitions.

See [docs/BUFFER_ARCHITECTURE.md](docs/BUFFER_ARCHITECTURE.md) for full design documentation.

## Project layout

```text
src/
  DeltaZulu.Agent.Core/
  DeltaZulu.Agent.Profiles/
  DeltaZulu.Agent.Kql/
  DeltaZulu.Agent.Outputs.Ndjson/
  DeltaZulu.Agent.Forwarder/
  DeltaZulu.Demo.Collector/
  DeltaZulu.Agent.Inputs.Syslog/
  DeltaZulu.Agent.Inputs.Files/
  DeltaZulu.Agent.Inputs.Auditd/
  DeltaZulu.Agent.Inputs.Windows/
  DeltaZulu.Buffer/
tests/
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

## Important implementation note

This archive is an implementation pass that should be compiled locally. The first compile pass should focus on the `Microsoft.Rx.Kql` and `Tx.Windows` API surfaces because those dependencies are old relative to the .NET 10 target.
