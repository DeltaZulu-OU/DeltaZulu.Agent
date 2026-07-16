# RealTimeKql/KqlTools vs DeltaZulu table handling

This note compares how Microsoft KqlTools/RealTimeKql handles KQL source table names with how DeltaZulu handles them today, using the `EtwTcp` and `EtwDns` examples as the motivating cases.

## Query examples

KqlTools sample queries start with source-specific table names:

```kql
EtwTcp
| where EventId in (10, 11)
| extend ProcessName = getprocessname(EventData.PID)
| extend SourceIpAddress = strcat(EventData.saddr, ":", ntohs(EventData.sport))
| extend DestinationIpAddress = strcat(EventData.daddr, ":", ntohs(EventData.dport))
| project SourceIpAddress, DestinationIpAddress, Opcode, ProcessName
```

```kql
EtwDns
| extend QueryResults = extract("(\\d+\\.\\d+\\.\\d+\\.\\d+)", 1, EventData.QueryResults)
| where isnotnull(QueryResults) and isnotempty(QueryResults)
| where isnotnull(EventData.QueryName) and isnotempty(EventData.QueryName)
| extend QueryName = EventData.QueryName
| project TimeCreated, QueryResults, QueryName
```

## KqlTools table model

KqlTools has a stream-first table model:

- Each input component chooses one logical KQL input name when it starts the Rx.Kql hub.
- The ETW command line uses the session name as the user-facing source selector, then passes an input stream to `EventProcessor`.
- `EventProcessor` calls `KqlNodeHub.FromFiles(inputStream, outputAction, inputName, queryFiles)` with that chosen input name.
- For an ETW session named `tcp`, the ETW component constructs the input name as `"etw" + sessionName`, producing `etwtcp`; KQL matching is case-insensitive, so sample queries can start with `EtwTcp`.
- The sample `EtwDns` query follows the same convention for a DNS ETW session name.

Important consequence: the query's leading table token is not a schema registry lookup. It is the stream alias supplied to Rx.Kql for the one observable currently being processed. The CLI/source binding decides which stream exists; the KQL file only names that stream.

Relevant KqlTools locations from `microsoft/KqlTools`:

- `Source/RealTimeKqlLibrary/EtwSession.cs`: `Start()` creates `Tx.Windows.EtwTdhObservable.FromSession(_sessionName)` and calls `Start(eventStream, "etw" + _sessionName, true, modifier)`.
- `Source/RealTimeKqlLibrary/EventComponent.cs`: the base component wires the input stream into `EventProcessor`.
- `Source/RealTimeKqlLibrary/EventProcessing/EventProcessor.cs`: `ApplyRxKql()` registers scalar functions and calls `KqlNodeHub.FromFiles(..., _inputName, ...)`.
- `Doc/Queries/Windows/SimplifyEtwTcp.kql` and `Doc/Queries/Windows/EtwDns.kql`: sample queries use `EtwTcp` and `EtwDns` as the first token.

## DeltaZulu table model

DeltaZulu currently separates resource identity, row shape, and the Rx.Kql observable alias:

- Resource profiles declare an input table in `profile.Input.Table`.
- `ResourceKqlProfileExecutor` always exposes the live observable to Rx.Kql as `Source`.
- Before writing the temporary query file consumed by Rx.Kql, `NormalizeQueryForRxKql(query, inputTableName, observableName)` rewrites occurrences of the declared input table to `Source` outside quoted strings.
- Each `SourceEvent` becomes one KQL row through `SourceEvent.ToKqlRow()`, preserving source-native fields, adding a `source` field from metadata when not already present, and adding nested `_metadata`.
- Schema discovery is handled separately through `SchemaDescriptor`/`SchemaTextParser`, where table names describe authoring/discovery contracts and known parser shapes rather than directly naming the Rx.Kql observable.

Important consequence: DeltaZulu's checked-in profile KQL can use a profile-facing table name such as `EventLog`, `Etw`, `Syslog`, or `Auditd`, but execution rewrites that table name to the internal `Source` observable. Unlike KqlTools, a provider-specific name such as `EtwTcp` or `EtwDns` is not automatically produced from the ETW provider/session binding unless the profile contract explicitly sets that as `profile.Input.Table`.

Relevant DeltaZulu locations:

- `src/DeltaZulu.Agent.Filter/Kql/ResourceKqlProfileExecutor.cs`: normalizes table names and executes all rows through the internal `Source` observable.
- `src/DeltaZulu.Pipeline.Core/Events/SourceEvent.cs`: materializes event fields, `source`, and `_metadata` for KQL.
- `src/DeltaZulu.Agent.SchemaMetadata/SchemaDescriptor.cs`: represents table/schema metadata for discovery.
- `src/DeltaZulu.Agent.SchemaMetadata/SchemaTextParser.cs`: parses known source-family schemas such as `WindowsEtw.Native`.

## Side-by-side comparison

| Concern | KqlTools/RealTimeKql | DeltaZulu |
| --- | --- | --- |
| Table source | Stream alias passed to `KqlNodeHub.FromFiles`. | Profile `input.table`, normalized to internal `Source`. |
| ETW table naming | Convention: `"etw" + sessionName` (`tcp` -> `EtwTcp`, `dns` -> `EtwDns`). | Generic family/profile table unless explicitly configured otherwise. |
| Runtime observable name | The source-specific alias itself. | Always `Source`. |
| Schema handling | Mostly inferred from dictionaries/dynamic event payload at runtime. | Separate schema descriptors for authoring and validation. |
| Source filtering | CLI/source component selects the stream before KQL executes. | Resource profile selects family/channel/session/provider; KQL filters rows inside that resource stream. |
| Query portability | Very concise for one bound live stream. | More explicit resource contracts; queries are less tied to session names but require table normalization. |

## Recommendation

DeltaZulu should introduce a unified local-resource catalog that resolves a displayed KQL table name to one resource configuration, schema, and live input. This is a DeltaZulu capability beyond KqlTools: KqlTools supplies one named observable selected by its CLI; it does not provide a persistent table catalog or multi-table query environment.

A concrete alias such as `EtwTcp`, `EtwDns`, `Security`, or `SyslogSshd` is a good workbench UX when it resolves unambiguously. Passing that alias directly to Rx.Kql matches KqlTools, but it is an implementation choice rather than the requirement. Resolving the alias to the correct binding and then safely rewriting the leading source expression to the internal `Source` observable is also valid. The existing `Source` behavior must remain compatible with profile execution.

This is better for DeltaZulu because it makes tables discoverable and executable in the same shape:

- The TUI schema tree can list local resource aliases that are actually queryable; public `schemas`/`resources` CLI commands are intentionally removed.
- A workbench query can start with exactly the table shown in the schema tree.
- The execution layer no longer needs to teach users that `EventLog` or `Etw` is rewritten to `Source`.
- Provider/session/channel-specific tables can carry specific schemas instead of forcing every ETW or Event Log resource through a generic family table.
- KqlTools examples become directly portable when a matching local resource binding exists.

The key design change is to make table binding explicit: a table name identifies both a local resource binding and the schema for rows produced by that binding.


## Cross-platform applicability

Yes. The table-binding model is intentionally platform-agnostic: a KQL table name maps to a local resource binding, and the binding opens the appropriate `ISourceInput` for the current host. The catalog entries differ by platform, but the user experience stays the same.

| Platform | Example local tables | Binding target | Schema source |
| --- | --- | --- | --- |
| Windows | `EtwTcp`, `EtwDns`, `Security`, `SysmonOperational` | ETW sessions/providers, Windows Event Log channels, EVTX/ETL replay files | `WindowsEtw.Native`, `WindowsEventLog.Native`, provider/channel metadata |
| Linux | `Auditd`, `AuditdSyscall`, `Syslog`, `SyslogSshd`, `SyslogSudo` | auditd log stream, syslog files/FIFO/TCP/UDP inputs, text/CSV files | `LinuxAuditd.AssembledEvent`, `LinuxSyslog.Native`, parser contracts |
| Any supported host | `Lines`, CSV/parser-specific table names | file-tail or replay inputs | file/parser schema contracts |

The roadmap should therefore avoid Windows-only names in core abstractions. `EtwTcp` and `EtwDns` are examples of Windows bindings, not the model itself. On Linux, the same machinery should let users discover `SyslogSshd` or `AuditdSyscall`, type that table name in KQL, and resolve it to the matching local input. The executor may expose that name directly to Rx.Kql or retain the internal `Source` alias after resolution.

Implementation guidance:

- Keep `TableBinding`, table resolution, and schema catalog code in platform-neutral assemblies.
- Put Windows discovery adapters behind Windows-specific inputs for ETW/Event Log.
- Put Linux discovery adapters behind Linux-specific inputs for auditd/syslog/file resources.
- Mark unavailable platform-specific tables as schema-only or omit them from executable local catalogs, rather than making query execution fail late.
- Preserve common fallback tables such as `Lines` for file-backed workflows on every platform.

## Roadmap to KqlTools-style local resource tables

### Phase 1: Introduce a table binding contract

Add a small model that describes a queryable local table:

| Field | Purpose | Example |
| --- | --- | --- |
| `Table` | KQL-visible table/observable name. | `EtwTcp` |
| `Aliases` | Additional accepted names for compatibility. | `Etw`, `Source` |
| `SourceKind` | Resource family used by input factories. | `windows.etw`, `linux.syslog`, `linux.auditd` |
| `Resource` | Concrete channel/session/provider/file binding. | ETW session `tcp` or provider `Microsoft-Windows-TCPIP` |
| `Schema` | Fields shown in `schemas` and used by validators. | `WindowsEtw.Native` plus provider payload fields |
| `Executable` | Whether the table can be opened on this host. | `true` for live session, `false` for schema-only |

`SchemaDescriptor` already has most of the schema-facing fields (`Table`, `Fields`, `SourceKind`, `Provenance`, `Confidence`, `Executable`). The missing piece is a runtime binding model that connects that descriptor to an `ISourceInput` factory.

### Phase 2: Build a local resource catalog

Create a catalog service that emits table bindings from the same places DeltaZulu already understands resources:

- Built-in parser contracts from `SchemaTextParser` for stable family tables.
- Profile files for enabled resource-specific bindings.
- Host discovery where available: Windows Event Log channels and ETW sessions/providers on Windows; auditd and syslog resources on Linux; files passed to `--tail` on any supported host.

The catalog should be the single source for `dzagentctl schemas`, TUI schema trees, query validation, and local query execution. If the schema UI shows `EtwTcp`, the executor should be able to open or attach to the same `EtwTcp` binding.

### Phase 3: Resolve the query table before execution

Add a resolver that parses the leading KQL table expression and matches it to the catalog. For the common single-source KqlTools pattern, the resolver can be deliberately simple:

1. Read the first pipeline input token.
2. Match it case-insensitively against `Table` and `Aliases`.
3. Return the selected table binding and a normalized query.
4. Reject unknown or schema-only tables with a clear error.

This preserves the simple KqlTools mental model while still giving DeltaZulu a place to enforce resource availability and profile policy.

### Phase 4: Bind the selected table safely to Rx.Kql

Change `ResourceKqlProfileExecutor` or add a workbench-specific executor path so a resolved table always executes against its selected binding. Passing the resolved name to `KqlNodeHub.FromFiles` makes the user-facing name match KqlTools. Retaining `Source` and rewriting only the resolved leading source expression is also acceptable, provided the schema tree, resolver, and input instance all refer to the same binding.

Compatibility behavior:

- Existing profiles can continue using `Source` through an alias or through the current rewrite path.
- New local/workbench queries should prefer the concrete table alias.
- Generic family tables such as `Etw` can remain aliases only when they are unambiguous for the selected resource.

### Phase 5: Attach schema metadata to the bound rows

Keep `SourceEvent.ToKqlRow()` as the row materialization boundary, but add table-binding metadata outside the row so the executor knows which schema/table produced it. The row should continue to preserve native fields, `source`, and `_metadata`; the table name should be execution context, not another projected field.

### Phase 6: Make schemas executable documentation

Update `dzagentctl schemas` and the TUI so each table entry answers three questions:

- What KQL table name do I type?
- What columns are available?
- Can this table run on this host right now, or is it schema-only?

That gives DeltaZulu the same simplicity as KqlTools with a richer local resource catalog.

### Phase 7: Tests and migration

Add tests that lock down the new behavior:

- `EtwTcp | ...` resolves to an `EtwTcp` binding and passes `EtwTcp` to Rx.Kql.
- `EtwDns | ...` resolves independently from `EtwTcp`.
- Existing `Source | ...` profile queries still execute.
- Unknown tables fail before source subscription.
- `schemas` output includes table names, aliases, schema confidence, and executable/schema-only state.

## Practical takeaway

KqlTools makes `EtwTcp` and `EtwDns` feel simple because those names are the current stream's Rx.Kql table aliases. DeltaZulu can use the same approach and improve on it by making aliases come from a local resource catalog backed by schema metadata. The target state is: users select or discover a local resource table, type that same table name in KQL, and the executor passes that name to Rx.Kql while opening the matching source binding.
