# dzagentctl Controller Roadmap

`dzagentctl` is the local controller. `dzagentd` is the daemon collector/forwarder.

The controller UI should be delivered first, then live tail, then simple local metrics. IPC is the final integration layer for sending commands to `dzagentd` and reading daemon state.

## Target CLI contract

```text
dzagentctl --tui [--profiles <profiles-dir>]
dzagentctl --tail "<query>" <path> [--input auto|syslog|auditd|csv|lines] [--limit 500]
dzagentctl --metrics
dzagentctl start|stop|restart|status|reload [-v] [--service <name>]
dzagentctl --help
dzagentctl --version
```

There are no public `schema`, `provider`, `run`, `syslog`, `eventlog`, `evtx`, `etl`, or `etw` subcommands.

## Phase 1: CLI contract cleanup

Goal: remove the legacy exploration CLI shape and make the controller contract explicit.

Deliverables:

- `Modes.cs`
- `CliOptions.cs`
- command router / mode dispatch cleanup
- README CLI section rewrite

Work:

- Remove public routing for old raw input commands.
- Remove public schemas, provider, and run concepts.
- Keep old input/pipeline logic only as internal reusable code where required.
- Ensure unknown or removed commands fail clearly.
- Keep `--help` focused on the controller contract only.

Acceptance criteria:

- `dzagentctl schemas` returns a clear removed/unknown command error.
- `dzagentctl provider` returns a clear removed/unknown command error.
- `dzagentctl syslog ...` returns a clear removed/unknown command error.
- `dzagentctl --help` shows only controller modes.
- `dzagentctl --version` works.

## Phase 2: KQL workbench TUI

Goal: implement the proper TUI first: schema tree on the left, KQL editor top-right, results grid bottom-right.

Deliverables:

- `Tui/KqlWorkbenchTui.cs`
- `Tui/TuiCatalogModel.cs`
- `Tui/TuiCatalogTreeBuilder.cs`
- `Tui/TuiQueryValidator.cs`
- `Tui/TuiQueryEngine.cs`
- `Tui/TuiResultGrid.cs`

Layout:

```text
┌ Data Catalog ───────────────┐ ┌ Query Editor ───────────────────────────┐
│ ▼ Local Resources            │ │ Processes                               │
│   ▼ Processes                │ │ | project name, memMB=workingSet/1024...│
│     id:int                   │ │ | order by memMB desc                   │
│     name:string              │ │ | take 20                               │
│     workingSet:long          │ └────────────────────────────────────────┘
│   ▼ Runtime                  │ ┌ Query Results ─────────────────────────┐
│   ▼ Environment              │ │ name              memMB                 │
│ ▼ Profile Schemas            │ │ dotnet            512                   │
│   ▼ Syslog                   │ │ dzagentctl        96                    │
└──────────────────────────────┘ └────────────────────────────────────────┘
Status: Ready | F5 Run | Ctrl+Q Quit | render disabled
```

Initial queryable tables:

- `Processes`
- `Runtime`
- `Environment`

Profile schemas may be displayed, but they must be marked as schema-only unless they have a bound local source.

Validation rules:

- Query must start with a known table.
- Unknown tables are rejected before execution.
- Schema-only profile tables are rejected before execution.
- Multiple statements are rejected.
- `render` is always rejected.

Keys:

- `F5` / `Ctrl+Enter`: run query
- `Ctrl+L`: clear results
- `Ctrl+Q`: quit
- `F1`: help
- `Enter` on table: insert table name into editor
- `Enter` on column: insert column name into editor

Acceptance criteria:

- `dzagentctl --tui` opens a real full-screen TUI.
- No console REPL remains.
- Left pane shows tables and columns as a tree.
- Top-right pane is editable KQL.
- Bottom-right pane shows tabular results.
- `render` is rejected with a clear message.

## Phase 3: Live tail TUI

Goal: make `--tail` obvious and useful: a continuously updated table of matching rows.

Deliverables:

- `Tail/TailMode.cs`
- `Tail/TailSourceBinder.cs`
- `Tail/TailQueryEngine.cs`
- `Tail/TailTableTui.cs`
- `Tail/TailRingBuffer.cs`
- `Tail/TailFormatDetector.cs`

Command:

```text
dzagentctl --tail "<query>" <path> [--input auto|syslog|auditd|csv|lines] [--limit 500]
```

Layout:

```text
┌ Tail Query ────────────────────────────────────────────────────────┐
│ Syslog | where Severity == "error" | project Timestamp, Hostname...│
└────────────────────────────────────────────────────────────────────┘
┌ Live Results ──────────────────────────────────────────────────────┐
│ Timestamp              Hostname       Severity   Message            │
│ 2026-07-06T18:42:01Z   srv01          error      Failed password... │
│ 2026-07-06T18:42:09Z   srv01          error      PAM auth failure... │
└────────────────────────────────────────────────────────────────────┘
Status: following /var/log/auth.log | read 2001 | matched 241 | shown 500 | Ctrl+Q quit
```

Behavior:

- Follow new records continuously.
- Apply KQL continuously.
- Append matching rows to a live table.
- Keep only the last `--limit` rows in memory.
- Show read, matched, parse error, dropped, and last event counters.
- Unknown files use `Lines(line:string, lineNumber:long)`.
- `render` is rejected.
- File rotation should be handled for log-like sources.

Acceptance criteria:

- `--tail` displays a continuously updating table.
- It does not dump NDJSON.
- It does not run as a one-shot file query.
- It remains responsive under high event volume.
- It uses bounded memory.

## Phase 4: Simple metrics model

Goal: define the data shape for a local troubleshooting dashboard before IPC exists.

Deliverables:

- `Metrics/AgentMetricsSnapshot.cs`
- `Metrics/AgentStatusSummary.cs`
- `Metrics/PipelineSummary.cs`
- `Metrics/ProfileStatusSummary.cs`
- `Metrics/BufferSummary.cs`
- `Metrics/OutputSummary.cs`
- `Metrics/FaultSummary.cs`
- `Metrics/IAgentMetricsProvider.cs`
- `Metrics/DisconnectedMetricsProvider.cs`
- `Metrics/StaticMetricsProvider.cs`
- `Metrics/SqliteMetricsStateProvider.cs`

Model:

```csharp
internal sealed record AgentMetricsSnapshot(
    AgentStatusSummary Agent,
    PipelineSummary Pipeline,
    IReadOnlyList<ProfileStatusSummary> Profiles,
    BufferSummary Buffer,
    OutputSummary Output,
    IReadOnlyList<FaultSummary> RecentFaults);
```

This should be current-state only. No metric history, alert rules, charts, Prometheus label model, or generic system monitoring.

Shared SQLite state before IPC:

- `dzagentd` may publish the latest `AgentMetricsSnapshot` into a small local SQLite state database.
- `dzagentctl` may open an occasional read-only connection to that database without participating in daemon write locks.
- Use SQLite WAL mode and a short `busy_timeout` so one daemon writer and one occasional controller reader can coexist cleanly.
- The database is current-state only; metric history belongs on the server, not in the agent.
- Keep the schema small and explicit, for example:
  - `agent_status`
  - `pipeline_summary`
  - `profile_status`
  - `buffer_summary`
  - `output_summary`
  - `recent_faults` bounded to a small count, such as 100 rows
- Writers should update rows transactionally and replace current state rather than append metric history.
- Readers should tolerate missing databases, missing tables, busy/locked reads, version mismatches, and stale timestamps by showing disconnected/stale state and retrying on the next refresh.
- The database should contain no raw event payloads and should be protected by local filesystem permissions.

Acceptance criteria:

- Metrics model is small and stable.
- Disconnected state is represented explicitly.
- Static provider can feed the metrics TUI for development.
- SQLite provider can read current state without blocking the daemon writer under normal one-writer/occasional-reader usage.
- No IPC dependency exists yet.

## Phase 5: Simple metrics dashboard TUI

Goal: implement `--metrics` as a local troubleshooting dashboard.

Screens:

- `F2` Overview
- `F3` Profiles
- `F4` Output
- `F5` Faults
- `Ctrl+R` Refresh
- `Ctrl+Q` Quit

Overview:

- Agent status, health, version, uptime, config path
- Read/sec, out/sec, filtered/sec, dropped/sec
- Last event time
- Last output time
- Profile summary
- Buffer summary
- Output status
- Recent warning/error count

Profiles:

```text
Profile ID | Input | Status | In/sec | Out/sec | Last Event | Last Error
```

Allowed statuses:

- `OK`
- `Idle`
- `Skipped`
- `Missing`
- `Error`
- `Degraded`

Output:

- Buffer depth
- Buffer bytes
- Oldest buffered record age
- Dead-letter count
- Backpressure state
- Active RELP endpoint
- Last ACK
- Retries
- Last output error

Faults:

```text
Time | Level | Component | Profile | Message
```

Keep a small recent-fault ring buffer, for example the last 100 records.

Acceptance criteria:

- `dzagentctl --metrics` opens a simple dashboard.
- It works with a disconnected/static provider.
- It does not pretend to show live daemon state before IPC exists.
- It is useful for local troubleshooting, not full observability.

## Phase 6: Daemon instrumentation

Goal: make `dzagentd` produce the simple metrics snapshot internally.

Deliverables:

- `Runtime/AgentMetricsRegistry.cs`
- `Runtime/AgentFaultRingBuffer.cs`
- `Runtime/ProfileRuntimeStatus.cs`
- `Runtime/InputRuntimeStatus.cs`
- `Runtime/OutputRuntimeStatus.cs`
- `Runtime/BufferRuntimeStatus.cs`
- `Runtime/SqliteMetricsStateWriter.cs`

Instrumentation points:

- daemon start/stop
- config load/reload
- profile load/skip/fail
- input read
- parse failure
- KQL filter input/output/error
- buffer enqueue/dequeue/dead-letter
- RELP connect/send/ack/retry/failure
- integrity monitor finding

Rules:

- Metrics updates must not block the pipeline.
- Keep counters simple.
- Keep strings bounded.
- Do not use raw event field values as metric dimensions.
- Profile ID, input family, and component name are acceptable.
- Fault messages should be operator-readable and short.

SQLite state publishing rules:

- Publish at a faster local cadence than console/file diagnostics, defaulting to 3 seconds or less, or on meaningful state changes with throttling.
- Store the latest current-state values only; do not store metric history.
- Use one daemon-owned write connection and short write transactions.
- Enable WAL mode so occasional `dzagentctl` reads do not block normal daemon updates.
- Keep all table schemas versioned and small.
- Keep the writer best-effort; metrics publishing failures must not fail collection/forwarding.

Acceptance criteria:

- `dzagentd` can produce an `AgentMetricsSnapshot` in-process.
- Metrics remain cheap under event load.
- Recent faults are available without reading log files.
- Optional SQLite state publishing lets `dzagentctl --metrics` read daemon state before IPC exists.

## Phase 7: Service lifecycle commands

Goal: make lifecycle commands real, but still separate from IPC control.

Deliverables:

- `Service/IAgentServiceController.cs`
- `Service/SystemdAgentServiceController.cs`
- `Service/WindowsServiceAgentServiceController.cs`
- `Service/UnsupportedServiceController.cs`
- `Service/ServiceControlCommand.cs`

Commands:

```text
dzagentctl start
dzagentctl stop
dzagentctl restart
dzagentctl status
dzagentctl reload
```

Behavior:

- Linux systemd uses `systemctl`.
- Windows uses Service Control Manager.
- Non-systemd Linux, containers, and unsupported service managers fail clearly.
- `reload` may initially map to service reload where supported, or return “not supported until IPC control is enabled.”

Acceptance criteria:

- Lifecycle commands work on systemd Linux.
- Lifecycle commands work on Windows SCM.
- Unsupported platforms fail with actionable messages.

## Phase 8: IPC commands and daemon data

Goal: connect `dzagentctl` to `dzagentd` for local read-only data first, then controlled local commands.

This is the final phase because the TUI and metrics screens should exist before the daemon protocol is designed around them.

Transport:

- Linux: Unix domain socket
- Windows: named pipe

Do not expose a TCP listener for local control. Do not make the first IPC version network-reachable.

Security rules:

- Local machine only.
- OS permissions restrict access.
- No unauthenticated TCP.
- No raw event payloads by default.
- Version every request and response.
- Separate read-only operations from mutating commands.
- Log command attempts in daemon diagnostics.

Protocol shape:

Use simple request/response messages first. Streaming can come later.

Request:

- `protocolVersion`
- `requestId`
- `operation`
- `arguments`

Response:

- `protocolVersion`
- `requestId`
- `success`
- `data`
- `errorCode`
- `errorMessage`

Message encoding can be JSON first for debuggability or MessagePack if consistency with internal delivery payloads is preferred. Start with JSON for local control IPC unless performance becomes a problem.

Read-only operations first:

- `Ping`
- `GetAgentStatus`
- `GetMetricsSnapshot`
- `GetRecentFaults`
- `GetEffectiveConfigSummary`
- `GetProfiles`
- `GetProfileStatus`

`dzagentctl --metrics` should call:

- `Ping`
- `GetMetricsSnapshot`

The KQL workbench should not require IPC in the first version. Later, it may use IPC to query daemon-known profile bindings or source state, but local snapshot queries should remain available offline.

Mutating operations later:

- `ReloadConfig`
- `EnableProfile`
- `DisableProfile`
- `RestartProfile`
- `FlushBuffer`
- `RetryDeadLetter`
- `ClearFaults`

Mutating commands should require explicit confirmation in the TUI or CLI where destructive.

Avoid these in the first IPC version:

- remote KQL execution over live telemetry
- raw event streaming
- arbitrary file reads
- arbitrary shell/service commands
- configuration file editing

IPC integration by command:

- `dzagentctl --metrics`: `GetMetricsSnapshot`, `GetRecentFaults`
- `dzagentctl status`: service manager status first, optionally `Ping`/`GetAgentStatus` if daemon is running
- `dzagentctl reload`: `ReloadConfig` over IPC when daemon supports it; service-manager reload fallback only if supported
- `dzagentctl --tui`: no IPC initially; later optional `GetProfiles`/`GetEffectiveConfigSummary` for profile catalog enrichment
- `dzagentctl --tail`: no IPC initially; remains a local file-tail troubleshooting tool

Acceptance criteria:

- `dzagentd` creates a local IPC endpoint with restrictive permissions.
- `dzagentctl` can detect daemon presence.
- `dzagentctl --metrics` can read live daemon metrics.
- Disconnected, permission denied, version mismatch, and daemon busy are handled cleanly.
- Read-only IPC works before any mutating command is added.
- `ReloadConfig` is the first mutating command.
- All mutating commands are logged by `dzagentd`.

## Final milestone order

| Milestone | Name | Outcome |
| --- | --- | --- |
| M1 | CLI contract cleanup | `dzagentctl` becomes a controller, not a raw collector CLI. |
| M2 | KQL workbench TUI | Real three-pane local KQL UI. |
| M3 | Live tail TUI | Continuously updated result table for file-backed troubleshooting. |
| M4 | Metrics model | Simple snapshot model for local dashboard. |
| M5 | Metrics dashboard TUI | Overview, Profiles, Output, Faults screens. |
| M6 | Daemon instrumentation | `dzagentd` produces current metrics and recent faults. |
| M7 | Service lifecycle commands | Start/stop/restart/status/reload through OS service manager. |
| M8 | IPC | Local protocol for daemon data and controlled commands. |

Confidence: 97%.
