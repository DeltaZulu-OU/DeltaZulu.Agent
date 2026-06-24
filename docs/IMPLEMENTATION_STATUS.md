# Implementation status

## Implemented in this archive

- Project split for Agent.
- .NET 10 project targets.
- `System.Reactive` based inputs.
- No internal observable classes.
- Resource profile YAML model and loader.
- KQL profile executor seam using `Microsoft.Rx.Kql`.
- NDJSON file and console sinks.
- Syslog TCP/file input with lightweight parsing.
- CSV file input using CsvHelper.
- Auditd parser and basic event assembler.
- Windows Event Log, EVTX, ETL, and ETW inputs using the original Tx.Windows approach.
- Sample profiles for sshd, PAM, auditd execve, Windows Security, Sysmon, and ETW kernel process.

## Not implemented yet

- Daemon, service lifecycle, installer, rsyslog/syslog-ng snippets.
- Enrichment and resource-local state providers.
- Full LAUREL-level auditd decoding and process tracking.
- Profile hot reload.
- Backpressure and local spooling.
- Golden fixture tests.
- Build validation in this environment.

## Explicitly out of scope

- DuckDB.
- Server-canonical normalization at the edge.
- A built-in syslog daemon replacement.
