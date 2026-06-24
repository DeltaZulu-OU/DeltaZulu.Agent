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
  winlog <logname>          Listen for new Windows Event Log events (Windows build).
  evtx <file.evtx>          Process an EVTX file (Windows build).
  etl <file.etl>            Process an ETL trace file (Windows build).
  etw <session>             Listen to a real-time ETW session (Windows build).

Outputs:
  json [file.ndjson]        Write DeltaZulu NDJSON to stdout or append to a file (default).
  table                    Print a compact console table.

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
dzagent syslog /var/log/auth.log table --profile profiles/linux/syslog/sshd.yaml
dzagent csv events.csv json out.ndjson --kql "Source | where RawMessage has 'sudo'"
dzagent syslogserver --address 127.0.0.1 --port 5514
dzagent schemas profiles json
```

Windows process creation examples:

```bash
# Sysmon process creation, Event ID 1, to console NDJSON.
dzagent winlog sysmon --kql "Source | where EventId == 1 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"

# Sysmon process creation, Event ID 1, to a compact console table.
dzagent winlog sysmon table --kql "Source | where EventId == 1 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"

# Windows Security process creation, Event ID 4688, to console NDJSON.
dzagent winlog Security --kql "Source | where EventId == 4688 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"

# Windows Security process creation, Event ID 4688, to a file sink.
dzagent winlog Security json security-4688.ndjson --kql "Source | where EventId == 4688 | project TimeCreated, ProviderName, EventId, EventData, Message, _metadata"
```

`winlog sysmon` expands to `Microsoft-Windows-Sysmon/Operational`. If that log is not present, install Sysmon or choose another available Windows Event Log channel before querying Event ID 1.
The CLI validates the requested Windows Event Log resource before starting KQL, so a missing optional source such as Sysmon reports a clear `error:` message instead of surfacing a Reactive/KQL exception stack.

Without a profile, source events pass through unchanged into the standard DeltaZulu NDJSON envelope.
With `--profile`, the CLI loads a DeltaZulu YAML resource profile and executes its KQL filter/select query through `DeltaZulu.Agent.Kql`.
With `--kql`, the CLI wraps the inline query in a temporary local resource profile so you can query an input in real time without creating a YAML file first. Output still defaults to console NDJSON, or can be routed to another sink with the existing output parameters such as `json out.ndjson` or `table`.
CLI options such as `--kql` can appear before or after the input command; for example, `dzagent --kql "Source | where EventId == 1" winlog Microsoft-Windows-Sysmon/Operational` is equivalent to placing `--kql` after the `winlog` arguments.

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

## Project layout

```text
src/
  DeltaZulu.Agent.Core/
  DeltaZulu.Agent.Profiles/
  DeltaZulu.Agent.Kql/
  DeltaZulu.Agent.Outputs.Ndjson/
  DeltaZulu.Agent.Inputs.Syslog/
  DeltaZulu.Agent.Inputs.Files/
  DeltaZulu.Agent.Inputs.Auditd/
  DeltaZulu.Agent.Inputs.Windows/
profiles/
  linux/
  windows/
docs/
```

## Important implementation note

This archive is an implementation pass that should be compiled locally. The first compile pass should focus on the `Microsoft.Rx.Kql` and `Tx.Windows` API surfaces because those dependencies are old relative to the .NET 10 target.
