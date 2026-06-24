# DeltaZulu Agent

Agent is a restructured version of the original RealTimeKql library. It is a daemon-consumable .NET library for resource-native event filtering and field selection using KQL-style YAML profiles.

This package is not a daemon, installer, SIEM, or server-side normalization engine. It now includes a thin CLI host for local exploration; a future daemon can host the same libraries and wire them to local syslog, Windows Event Log, ETW, auditd plugin input, files, and output sinks.


## Command line tool

DeltaZulu now includes a small `dzagent`-style console host in `src/DeltaZulu.Agent.Cli`.
It is intentionally thin: the executable wires the existing input libraries, KQL profile executor, pipeline helper, and NDJSON output sinks together so local event exploration has the same resource-profile behavior as daemon hosts.

```text
Usage: dzagent <input> [<arg>] [<output> [<arg>]] [--profile <profile.yaml>]

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
  --profile, -q, --query    Apply a DeltaZulu YAML resource profile containing KQL.
  --address <ip>            syslogserver bind address.
  --port <port>             syslogserver TCP port.
```

Examples:

```bash
dzagent syslog /var/log/auth.log table --profile profiles/linux/syslog/sshd.yaml
dzagent csv events.csv json out.ndjson --profile profiles/linux/syslog/pam.yaml
dzagent syslogserver --address 127.0.0.1 --port 5514
```

Without a profile, source events pass through unchanged into the standard DeltaZulu NDJSON envelope.
With `--profile`, the CLI loads a DeltaZulu YAML resource profile and executes its KQL filter/select query through `DeltaZulu.Agent.Kql`.

## Current restructuring goals

- Rename from `RealTimeKqlLibrary` to `DeltaZulu.Agent.*`.
- Target .NET 10.
- Remove custom observable classes and use `System.Reactive` directly.
- Preserve ETW, ETL, EVTX, and Windows Event Log capabilities from the original library.
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

This archive was generated in an environment without the .NET SDK, so it has not been restored or compiled here. Treat it as an implementation pass that must be compiled locally. The first compile pass should focus on the `Microsoft.Rx.Kql` and `Tx.Windows` API surfaces because those dependencies are old relative to the .NET 10 target.
