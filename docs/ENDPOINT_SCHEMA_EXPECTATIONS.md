# Endpoint schema expectations

This document defines the minimum source tree and query-authoring behavior the
TUI should expose for Windows and Linux endpoints. The workbench tree is a
schema/navigation aid, not a profile browser: profiles may still provide runnable
bindings, but the user-facing information model is `log source -> fields`, similar
to `table -> columns` in a database.

## Common TUI model

The TUI schema tree has a virtual root named `none`. The root's children are
primary log sources. Each primary source child contains field-name children that
can be used while authoring KQL.

Selecting a primary source should insert a source predicate for that table. At
minimum, source predicates use the source name carried by the parsed row:

```kql
<Table> | Source ~= "<primary-source>"
```

Selecting a field should insert the field name. Field names that are not simple
KQL identifiers should be bracket-quoted.

The source tree must not present profiles as the primary source of truth. Profiles
can be used as temporary discovery or execution hints until parser- and endpoint-
inventory-backed schema discovery is available, but the tree's visible nodes
should be source families, source names, and field names.

## Windows endpoints

Minimum Windows source families:

| Family | Primary source node | Field children come from | Inserted source snippet example | Notes |
| --- | --- | --- | --- | --- |
| Windows Event Log | Channel name, such as `Security`, `System`, or `Microsoft-Windows-AppLocker/EXE and DLL` | Windows Event Log parser fields, including envelope fields and named `EventData` fields when known | `Eventlog | Source ~= "Security" ` | Channels that conventionally end in `/Operational` may be displayed without the suffix when that is the useful product/source name. |
| ETW | Provider/source name, such as `Windows-Kernel-Process` or `Microsoft-Windows-Kernel-Process` | ETW envelope fields plus provider payload fields when parser or manifest metadata is available | `ETW | Source ~= "Windows-Kernel-Process" ` | ETW session names are runtime bindings, not the source node. Provider/source is the source node. |
| EVTX replay | Channel or log identity from the file, when available; otherwise the file binding remains an execution detail | Same parser contract as Windows Event Log for event records | `Eventlog | Source ~= "Security" ` when channel identity is known | EVTX files are a replay transport for Windows Event Log-shaped data, not a separate semantic source family in the tree when channel metadata is available. |
| ETL replay | Provider/source name, when available | Same parser contract as ETW records | `ETW | Source ~= "Microsoft-Windows-Kernel-Process" ` | ETL files are a replay transport for ETW-shaped data, not a separate semantic source family when provider metadata is available. |

Minimum Windows field expectations:

- Event Log rows should expose stable envelope fields such as timestamp, event ID,
  provider/source, channel/source, computer/host, record ID when available, level,
  task, opcode, keywords, raw XML where preserved, and named event data fields.
- ETW rows should expose native identity/envelope fields such as provider name,
  provider GUID, event ID, opcode, version, process ID, thread ID, timestamp,
  activity IDs when available, and parser-discovered payload fields.
- Windows parser output should preserve source-native field names. Any future
  canonical/server-side names should be additive and provenance-documented, not a
  replacement for native fields in the TUI tree.

## Linux endpoints

Minimum Linux source families:

| Family | Primary source node | Field children come from | Inserted source snippet example | Notes |
| --- | --- | --- | --- | --- |
| auditd | Audit record type / syscall type, such as `SYSCALL`, `EXECVE`, `PATH`, or a syscall-focused grouping when the parser emits one | auditd parser and assembler fields | `Auditd | Source ~= "SYSCALL" ` | auditd often groups multiple physical records into one logical event; the tree should expose the primary type/group plus fields available on the assembled event. |
| syslog | Application/process name, such as `sshd`, `sudo`, `kernel`, or another parsed app name | syslog parser fields plus parser-extracted application payload fields | `Syslog | Source ~= "sshd" ` | Syslog facility is useful metadata, but the minimum primary tree node is the application because that is the operational source users filter on most often. |
| text/CSV/other file parsers | Parser-defined source identity | Parser contract for that file type | `<Table> | Source ~= "<parser-source>" ` | For sources outside auditd and syslog, the source identity depends on how the parser defines meaningful primary source. |

Minimum Linux field expectations:

- auditd rows should expose record timestamp, serial/audit ID, record type(s),
  syscall fields, success/result, process and user IDs, executable/command fields,
  key fields, assembled multi-record fields such as `EXECVE`/`PATH` when present,
  and the raw record text where preserved.
- syslog rows should expose timestamp, hostname, application/process name, process
  ID, facility, severity, message, structured data for RFC 5424 where present,
  parser-extracted key/value payload data where available, and the raw message.
- Linux parser output should preserve source-native field names and parser-owned
  nested structures. Server-side normalization should not erase the parser fields
  shown in the TUI tree.

## Discovery and fallback behavior

The minimum source tree can be assembled from the parser contracts listed above.
When live endpoint inventory becomes available, it should refine the source list
with observed channels, providers, audit record types, and syslog applications.
When no inventory exists, the TUI should still show the parser-defined minimum
schema and allow users to type or paste source predicates manually.

Unknown or newly supported parsers must declare:

1. the KQL table name;
2. the primary source identity field represented by `Source`;
3. the minimum stable field names; and
4. whether any source suffixes, aliases, or transport-specific bindings should be
   hidden from the user-facing source node.

## Type-fidelity expectations

The endpoint schema tree presents fields, but field names alone are not the type
contract. Each visible field should resolve to a schema-registry logical type
before it is considered production-stable. The registry entry records nullability,
canonical timestamp precision, duration units, numeric width/decimal scale, and
specialized logical annotations such as UUID, IP, MAC, enum, nested object,
variant, or geospatial carrier.

The TUI may initially discover fields from parser contracts or endpoint
inventory, but it should not infer physical sink types independently. Query
authoring hints, backend DDL, Avro wire schemas, Arrow server schemas, and JSON
edge projections all derive from the same registry entry so Proton and DuckDB do
not drift into incompatible interpretations of the same field.
