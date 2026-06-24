# DeltaZulu Agent

Agent is a restructured version of the original RealTimeKql library. It is a daemon-consumable .NET library for resource-native event filtering and field selection using KQL-style YAML profiles.

This package is not a daemon, CLI, installer, SIEM, or server-side normalization engine. A future daemon can host this library and wire it to local syslog, Windows Event Log, ETW, auditd plugin input, files, and output sinks.

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
