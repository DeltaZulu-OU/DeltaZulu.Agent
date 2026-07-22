# Gap analysis: DeltaZulu local resources as real-time KQL tables vs KqlTools

This gap analysis compares DeltaZulu.Agent with `microsoft/KqlTools` specifically for two capabilities:

1. Gathering local log resources as real-time KQL tables.
2. Creating streaming queries over those tables.

It builds on the table-handling comparison in `docs/REALTIMEKQL_TABLE_HANDLING_COMPARISON.md` and focuses on implementation gaps rather than general architecture.

## Status snapshot

This document was refreshed on 2026-07-21 against the current DeltaZulu.Agent tree and the archived `microsoft/KqlTools` repository. The upstream KqlTools repository is read-only as of 2026-06-15, so `master` is treated as the stable RealTimeKqlLibrary reference point.

## Scope correction

KqlTools gives a single CLI-selected observable a KQL name. Its `EtwTcp` and `EtwDns` examples are aliases for ETW session streams, not provider-specific schema-registry tables: a session may contain events from multiple providers. It does not demonstrate a persistent catalog, resource resolver, or multiple simultaneously addressable live tables. DeltaZulu should use it as a single-stream authoring reference, not as a complete catalog architecture. Differences from that reference must be deliberate: `DeltaZulu.Parse` should replace application-specific parsing for raw/non-structured logs such as syslog, auth.log, audit logs, web-server logs, and arbitrary file text, but it must not accidentally replace the user-facing table alias model.

## Baseline: what KqlTools does well

KqlTools/RealTimeKql has a simple streaming pipeline:

```text
input command -> EventComponent -> IObservable<IDictionary<string, object>>
              -> EventProcessor -> KqlNodeHub.FromFiles(inputName, query files)
              -> output sink
```

Important properties:

- Every input reader owns a stream name that becomes the KQL table name.
- ETW uses the convention `"etw" + sessionName`, so a `tcp` session becomes an `EtwTcp` table and a `dns` session becomes an `EtwDns` table.
- File inputs use the file stem as the stream/table name.
- Query files are standing streaming queries over the observable.
- Output is pluggable: console JSON, console table, ADX, blob, and Windows Event Log outputs.

Relevant KqlTools code locations:

- `Source/RealTimeKqlLibrary/EventComponent.cs` wires each input observable into either direct output or `EventProcessor`.
- `Source/RealTimeKqlLibrary/EventProcessing/EventProcessor.cs` calls `KqlNodeHub.FromFiles(_inputStream, _output.KqlOutputAction, _inputName, queries)`.
- `Source/RealTimeKqlLibrary/EtwSession.cs` passes `"etw" + _sessionName` as the Rx.Kql input name.
- `Source/RealTimeKqlLibrary/SyslogFileReader.cs` and `Source/RealTimeKqlLibrary/CsvFileReader.cs` derive a table name from the file name.
- `Source/RealTimeKql/Program.cs` binds CLI input subcommands (`etw`, `etl`, `winlog`, `evtx`, `csv`, `syslog`, `syslogserver`) to event components and output subcommands.

## Baseline: what DeltaZulu already has

DeltaZulu already has many of the raw ingredients:

- Cross-platform `ISourceInput` abstractions that emit `SourceEvent` rows.
- Windows inputs for ETW, ETL, Event Log, and EVTX in Windows builds.
- Linux/local inputs for syslog, auditd, CSV, lines, FIFO/TCP syslog, and file-tail workflows.
- A profile executor that streams `SourceEvent` rows through Rx.Kql, registers custom scalar functions, surfaces Rx.Kql parse/runtime failures, and still exposes the observable as `Source`.
- Query normalization that rewrites a profile-declared input table to `Source` outside quoted strings, preserving existing profile behavior but preventing concrete aliases from reaching Rx.Kql.
- A workbench source registry that can bind profiles or tail-mode inputs to runnable sources. It creates `BoundWorkbenchSource` instances for syslog, auditd, CSV, lines, and Windows-only Event Log/EVTX/ETL/ETW adapters when built on Windows.
- Schema descriptors and parser contracts for local authoring.
- TUI and tail modes that can run live queries and display bounded result tables with read/matched/error/displayed counters and last-event timestamps.

Relevant DeltaZulu code locations:

- `src/DeltaZulu.Pipeline/Core/Abstractions/ISourceInput.cs` defines the observable source interface.
- `src/DeltaZulu.Agent.Filter/Kql/ResourceKqlProfileExecutor.cs` streams `SourceEvent.ToKqlRow()` dictionaries into `KqlNodeHub.FromFiles`.
- `src/DeltaZulu.Agent.ProfileWorkbench/WorkbenchSourceRegistry.cs` maps profiles and `--tail` inputs to concrete `ISourceInput` instances and carries source display/table/schema data in `BoundWorkbenchSource`.
- `src/DeltaZulu.Agent.ProfileWorkbench/WorkbenchQueryRunner.cs` runs finite and live workbench queries over bound sources, cloning each query into a profile whose input table is the bound table.
- `src/DeltaZulu.Agent.ProfileWorkbench/WorkbenchSchemaTree.cs` builds the current schema/source tree for the TUI and still inserts `<Table> | Source ~= "<source>"` for source nodes.
- `src/DeltaZulu.Agent.SchemaMetadata/SchemaDescriptor.cs` and `SchemaTextParser.cs` describe known table schemas.

## Core gap summary

DeltaZulu already gathers resources as `IObservable<SourceEvent>` streams, binds selected profiles to `ISourceInput` instances in the workbench, and executes live KQL. The gap is not basic ingestion. The missing host-wide abstraction is a single resolver that ties a logical table name, schema, concrete resource configuration, and a shared live input instance together. Today, resource/profile schema, source selection, and the Rx.Kql observable name remain separate concerns, with execution normalized to `Source`.

The workbench schema tree now presents configured sources as `<table> (source: <name>)` and columns as `<name>: <KQL type>`, including the runtime `source:string` and `_metadata:dynamic` fields. Workbench binding now reaches concrete live adapters through `BoundWorkbenchSource`, but those bindings remain workbench-local and profile/tail initiated. They are intentionally not yet a host-wide executable table catalog, and the executor still normalizes to `Source`.

## Gap matrix

| Area | KqlTools behavior | DeltaZulu today | Gap | Recommended DeltaZulu target |
| --- | --- | --- | --- | --- |
| Table identity | Input stream name is the KQL table (`EtwTcp`, `EtwDns`, file stem) for one CLI-selected stream. | Profile `input.table` is rewritten to internal `Source`; workbench/tail retain table/schema/source data only inside `BoundWorkbenchSource`. | Table identity is fragmented; arbitrary queries cannot resolve a host resource by table name alone, and Rx.Kql never sees the concrete table alias in the current executor. | Make table binding explicit. Directly passing the logical name to Rx.Kql is optional for compatibility paths, but new local-query execution should resolve to the same live resource and prefer the logical table name. |
| Resource discovery | CLI command selects exactly one input resource. | Profiles and `--tail` can bind inputs through `WorkbenchSourceRegistry`, including platform-gated Windows adapters and Linux/local file adapters, but there is no single executable resource catalog. | Schema display, source binding, and execution are closer in the workbench than before, but still not unified around a reusable catalog. | Add a local resource catalog that emits executable and schema-only `TableBinding` entries. |
| Real-time table gathering | Event components expose live observables directly to KQL. | `ISourceInput` exposes live observables, but binding is profile/workbench-specific. | The runtime can gather streams, but not yet as first-class named tables independent of profiles. | Promote `BoundWorkbenchSource` into a general `TableBinding` + `ISourceInput` factory. |
| Streaming query lifecycle | Query files become standing subscriptions until completion or Ctrl+C. | Workbench supports live subscriptions with counters; daemon profiles run continuously; tail mode displays live bounded rows. | Query lifecycle and counters are implemented in multiple paths instead of one streaming-query API. | Introduce a shared streaming query service used by TUI, tail, and daemon-local diagnostics. |
| Schema availability | Mostly inferred from event dictionaries; low ceremony. | Stronger schema descriptors exist, but provider/source-specific table schemas are not always executable. | DeltaZulu has schema metadata but not enough binding metadata to say “this schema is runnable now.” | `schemas`/TUI should show table, aliases, columns, and executable/schema-only state. |
| Cross-platform input model | Windows-heavy with syslog/csv support. | Cross-platform inputs exist; Windows-specific inputs are conditionally compiled; `DeltaZulu.Parse` is the planned parser-materialization path for raw/non-structured text sources. | Table naming examples are currently Windows-centric, and parser migration could accidentally collapse concrete aliases into generic family tables. | Use platform-neutral table bindings with Windows, Linux, and local raw discovery adapters. Keep KqlTools-style table aliases (`EtwTcp`, `EtwDns`, file stems, plus deliberate raw-log aliases such as `AuthLog` or `NginxAccess`) separate from Parse materialization. |
| Query authoring | Query starts with the actual stream alias. | TUI insertion currently emits `<Table> | Source ~= "<source>"` for some profile sources. | The inserted query is more complex than KqlTools and exposes internal source filtering. | Insert only the concrete table alias when a table binding is unambiguous. |
| Output model | Streaming output sinks are pluggable. | Agent pipeline forwards records; workbench/TUI displays tables; durable/RELP output exists for daemon paths. | Local streaming query output is not yet a pluggable sink model like KqlTools. | Define output sinks for local streaming queries: TUI table, NDJSON, MessagePack/RELP, ADX/export later. |
| Multi-resource queries | Primarily one input observable per command/query. | Current profile executor is also effectively one source stream. | Multi-table joins are not the immediate KqlTools parity target. | First match KqlTools single-stream behavior; defer multi-table joins/catalog composition. |

## Detailed gaps and actions

### Phase 0: Correctness and production parity

**Current status:** Partially addressed. The profile executor now has stronger query-normalization and Rx.Kql failure handling, and the workbench runner tracks live counters. The remaining Phase 0 concern is not whether streams can run, but whether all profile/daemon/workbench paths share the same source factory and standing-query semantics.

Before adding a catalog, preserve correct effective profile contracts:

- An omitted `input` block must allow family defaults (`Etw`, `EventLog`, known native schemas) to apply. `ResourceInputContract.Table` therefore defaults to empty, and the YAML loader owns default application.
- Add tests that load checked-in profiles—especially `profiles/windows/etw/tcpip.yaml`—rather than only constructing profiles in memory.
- Consolidate daemon and workbench input factories. The daemon currently has a narrower input switch and fixed Linux syslog/auditd paths; it must not silently diverge from available `ISourceInput` adapters.
- Verify `summarize`/`bin` behavior, hot-reload state reset, failure isolation, and bounded-memory policies before treating stateful KQL as a supported standing-query contract.
- Keep the current Rx.Kql parse/runtime failure surfacing as a baseline when moving execution behind a shared streaming service.

### 1. Executable table catalog

**Current status:** Still open. `BoundWorkbenchSource` is the closest implementation artifact, but it is scoped to the profile workbench and directly embeds an `ISourceInput` rather than exposing catalog metadata plus an opener/factory.

**Gap:** DeltaZulu can describe schemas and bind workbench sources, but there is no single catalog object that says: “`SyslogSshd` is a runnable table on this host, with these aliases, this schema, and this input factory.”

**Action:** Add a `TableBinding` model and catalog service:

```text
TableBinding
- Table: string
- Aliases: IReadOnlyList<string>
- SourceKind: string
- Platform: windows|linux|local|any
- ResourceIdentity: provider/channel/session/path/service
- Schema: SchemaDescriptor
- Executable: bool
- Open: Func<CancellationToken, IObservable<SourceEvent>>
```

This should subsume `BoundWorkbenchSource` for local query execution while allowing profile-derived and host-discovered bindings to live side by side.

### 2. Table-first query resolution

**Current status:** Still open and now explicit in code. `WorkbenchQueryRunner` passes the bound table into the cloned profile, but `ResourceKqlProfileExecutor` immediately normalizes that table to the hard-coded `Source` observable before calling `KqlNodeHub.FromFiles`.

**Gap:** `ResourceKqlProfileExecutor` currently exposes Rx.Kql input as `Source`, then normalizes the profile table to `Source` before execution. That prevents KqlTools-style queries from using concrete table names as the actual Rx.Kql input names.

**Action:** Add a resolver before execution:

```text
query text -> leading table token -> TableBinding -> executable input -> Rx.Kql observable name
```

For `EtwTcp | where ...`, the resolver should select the `EtwTcp` binding and pass `EtwTcp` to `KqlNodeHub.FromFiles`. `Source` should remain a compatibility alias, not the default observable name for new local queries.

### 3. Cross-platform resource discovery

**Current status:** Partially addressed for manual binding. `WorkbenchSourceRegistry` can open Linux/local file sources and Windows-only ETW/Event Log/EVTX/ETL sources behind conditional compilation, but discovery remains profile/tail driven rather than host-catalog driven.

**Gap:** DeltaZulu has platform-specific inputs, but table discovery is not yet a unified cross-platform operation.

**Action:** Add discovery adapters that contribute to the same catalog:

- Windows adapter: Event Log channels, ETW sessions/providers, EVTX/ETL replay bindings.
- Linux adapter: auditd log stream, syslog files/services, auth.log, FIFO/TCP syslog bindings, and other raw text sources. The acquisition adapter supplies collection metadata and a common `raw` payload; `DeltaZulu.Parse` owns field materialization through patterns.
- Local/file adapter: `Raw`, `Lines`, `Csv`, parser-detected file tables, and KqlTools-style file-stem aliases.
- Profile adapter: checked-in profiles as schema-only or executable bindings depending on available resource configuration.

Core catalog and query-resolution code must stay platform-neutral; only discovery/opening adapters should be platform-specific.

### 4. Schema as runnable documentation

**Current status:** Partially addressed for visibility, open for table-first authoring. The schema tree includes runtime fields and source nodes, but its insertion text still emits a family table plus `Source ~= ...` when source metadata is present.

**Gap:** DeltaZulu schemas are useful for authoring, but they do not yet consistently represent runnable tables. The current TUI schema tree can insert a family table plus `Source ~= ...`, which is not as direct as KqlTools.

**Action:** Change schema presentation to show table bindings:

```text
EtwTcp          executable  WindowsEtw.Native + payload fields
EtwDns          executable  WindowsEtw.Native + payload fields
AuthLog         executable  raw + Parse pattern fields
NginxAccess     executable  raw + Parse pattern fields
AuditdSyscall   executable  raw + Parse pattern fields
Lines           executable  lineNumber:long,line:string
```

Selecting a table should insert `EtwTcp | `, `EtwDns | `, or `AuthLog | `, not a generic table plus source predicate, unless the binding is intentionally a family table.

### 5. Streaming query service

**Current status:** Still open. `WorkbenchQueryRunner` is a useful nucleus because it owns validation, finite/live subscriptions, row limits, counters, and errors for the workbench, but it is not yet a shared service used by daemon or local diagnostics.

**Gap:** KqlTools has one obvious streaming-query path. DeltaZulu has daemon profiles, workbench live mode, and tail mode with overlapping but separate execution paths.

**Action:** Introduce a shared service:

```text
StreamingQueryService.Run(TableBinding binding, string query, StreamingQueryOptions options)
```

The service should own query validation, Rx.Kql startup, counters, cancellation, errors, and output sink fan-out. TUI, `--tail`, and daemon diagnostics can call it rather than each rebuilding the pipeline.

### 6. Output sinks for streaming queries

**Current status:** Still open. Local query output is still coupled to workbench/TUI result collection, while production delivery uses separate output contracts.

**Gap:** DeltaZulu has production delivery outputs and TUI display outputs, but not a KqlTools-like local streaming query sink abstraction.

**Action:** Define lightweight local sinks:

- TUI table sink.
- Console table sink.
- NDJSON sink.
- File sink.
- Optional future ADX/export sink.
- Optional bridge to existing durable/RELP delivery when a query is promoted to a profile.

### 7. Compatibility and migration

**Gap:** Existing profiles and tests expect `Source` normalization behavior.

**Action:** Keep compatibility deliberately:

- Continue supporting `Source | ...` for existing resource profiles.
- Add aliases from old family tables to concrete table bindings when unambiguous.
- Warn when a query uses `Source` in the local workbench and a concrete table alias is available.
- Add migration docs showing `Etw | Source ~= "Tcp"` becoming `EtwTcp | ...` where possible.

## Prioritized implementation roadmap

| Priority | Work item | Outcome |
| --- | --- | --- |
| P0 | Create `TableBinding` model and local catalog interface. | One representation for queryable real-time tables, with aliases named in the KqlTools style (`EtwTcp`, `EtwDns`, file stems) where applicable and raw/non-structured sources represented by a common `raw` payload type. |
| P0 | Add query table resolver for single-source pipeline queries. | `EtwTcp | ...` and `EtwDns | ...` select bindings before execution; raw-log aliases such as `AuthLog | ...` or `NginxAccess | ...` are deliberate DeltaZulu extensions whose fields come from Parse patterns. |
| P0 | Teach executor/workbench path to pass resolved table name to `KqlNodeHub.FromFiles`. | Rx.Kql sees the concrete table alias. |
| P1 | Move `WorkbenchSourceRegistry` binding logic behind the catalog. | TUI/tail/source binding use one table catalog. |
| P1 | Update schema tree insertion to use table aliases directly. | Users type what they see in the schema tree. |
| P1 | Add Windows and Linux discovery adapters. | Catalog works on both Windows and Linux. |
| P2 | Add `StreamingQueryService` and local output sinks. | TUI, tail, and local diagnostics share one streaming-query pipeline. |
| P2 | Add migration warnings/docs for `Source` and family-table queries. | Existing profiles remain compatible while new authoring gets simpler. |
| P3 | Consider multi-table joins or multiple concurrent bindings. | Future platform-style local analytics, not required for KqlTools parity. |

## Acceptance tests

Add tests that prove the gap is closed:

- Catalog returns executable `EtwTcp`/`EtwDns` bindings on Windows when the resources are available or schema-only entries when not available.
- Catalog returns executable raw/non-structured log bindings such as `AuthLog`, `NginxAccess`, `Syslog`, `Auditd`, or file-stem aliases when configured paths/resources exist, while `DeltaZulu.Parse` supplies their parser-derived fields from patterns.
- `EtwTcp | where EventId in (10, 11)` resolves `EtwTcp` and passes `EtwTcp` to Rx.Kql.
- `AuthLog | where Severity == "error"` resolves a raw auth.log binding and passes `AuthLog` to Rx.Kql after Parse materialization.
- Existing `Source | ...` profile queries still execute through compatibility aliases.
- Unknown tables fail before opening source inputs.
- Schema tree selection inserts the concrete table name.
- Live query counters report read, matched, error, and last-event values consistently across TUI and tail mode.

## Bottom line

DeltaZulu is closer than it looks: it already has real-time source inputs, schema metadata, Rx.Kql execution, and live TUI/tail query paths. The main gap is not collection capability; it is the absence of a unified table-binding layer that makes gathered resources appear as first-class KQL tables. Closing that gap will give DeltaZulu the KqlTools simplicity of `EtwTcp | ...`, `EtwDns | ...`, and raw-log aliases such as `AuthLog | ...` while retaining DeltaZulu's stronger cross-platform resource metadata, Parse-based extraction, schemas, and production pipeline integration.
