# Auditd LAUREL-style KQL transformations in DeltaZulu.Agent

Yes. DeltaZulu.Agent can handle the same *class* of auditd transformation shown in the Sentinel/LAUREL workflow, but the responsibility is split differently from Microsoft Sentinel:

- The auditd input parses native audit lines, groups records that share the same audit event ID, and emits one assembled `SourceEvent` per audit event.
- The assembled event keeps source-native record groups such as `SYSCALL`, `EXECVE`, `PATH`, and `PROCTITLE` as nested fields; `CWD` is present only when the host audit rules do not exclude `msgtype=CWD`.
- Agent profile KQL should filter and select source events, not permanently normalize or canonicalize them at the edge.
- The default checked-in auditd profile keeps all assembled auditd events rather than limiting collection to `execve`; exec-specific command-line reconstruction is an ad hoc or downstream query concern.
- Host audit rules still decide which record types exist. For example, if auditd excludes `msgtype=CWD`, Agent KQL should not require `CWD.cwd` for the default transformation.
- Presentation fields such as a Sentinel parser's `full_cli`, `EXE`, `USER`, or `Key_` can be expressed as ad hoc CLI/workbench KQL or server-side/platform KQL over the already assembled nested auditd event.

## Mapping from the Sentinel/LAUREL example

The referenced LAUREL flow first combines auditd records into a JSON object and then uses Sentinel KQL to extract easier-to-query fields from JSON. DeltaZulu.Agent already performs the combine step in its auditd assembler. For example, an audit event can be queried as nested KQL fields directly:

```kql
EventLog
| where source =~ "auditd"
| where SYSCALL.SYSCALL == "execve" or SYSCALL.syscall == 59
| extend user = tostring(SYSCALL.UID)
| extend exe = tostring(SYSCALL.exe)
| extend audit_key = tostring(SYSCALL.key)
| extend argv_from_execve = strcat_array(EXECVE.ARGV, " ")
| extend argv_from_proctitle = strcat_array(PROCTITLE.ARGV, " ")
| extend full_cli = iff(isnotempty(argv_from_proctitle), argv_from_proctitle, argv_from_execve)
```

For checked-in resource profiles, keep this as a filter unless the output is explicitly an exploration/export workflow. A production profile should normally preserve the native auditd shape and let downstream detection or server-side KQL do semantic naming.

## Agent KQL subset support for this query

The query above is intended to run in the Agent profile/CLI KQL executor. It uses the Agent-supported pipeline operators `where`, `extend`, and `project`; native scalar conversion such as `tostring`; and Agent-registered scalar helpers for `strcat_array` and `isnotempty` so the LAUREL-style command-line reconstruction does not depend on a full Microsoft Sentinel runtime.

This is sufficient for the Agent-native form of the transformation, where auditd records have already been assembled into nested fields before KQL runs. It is not a promise that arbitrary Microsoft Sentinel parser functions can be pasted into the Agent unchanged. The original Sentinel parser works around syslog-delivered JSON strings with functions such as `parse_json`, `pack_array`, `replace_strings`, `dynamic`, and `project-away`; those are either unnecessary after Agent auditd assembly or outside the intentionally small profile-KQL subset. If a future profile needs one of those semantics at the edge, add a narrow Agent scalar helper or parser feature and cover it with an executor test before documenting it as supported.

| Sentinel parser concern | Agent-native equivalent | Covered by current Agent path? |
| --- | --- | --- |
| Combine auditd records by event ID | `AuditdEventAssembler` emits one `SourceEvent` per audit ID | Yes |
| Parse JSON out of `SyslogMessage` | Native auditd parser emits nested records such as `SYSCALL`, `EXECVE`, `PATH`, and `PROCTITLE`; `CWD` depends on host audit rules | Yes, no `parse_json` needed |
| Join `EXECVE.ARGV` into a command line | `strcat_array(EXECVE.ARGV, " ")` | Yes |
| Prefer `PROCTITLE.ARGV` when available | `iff(isnotempty(argv_from_proctitle), argv_from_proctitle, argv_from_execve)` | Yes for the documented query |
| Remove intermediate columns | Use `project` to emit the desired fields, or preserve source shape in production profiles | Yes for projection; profile guidance prefers preserving shape |
| Full Sentinel parser compatibility | Paste arbitrary Sentinel KQL parser functions unchanged | No |

## Supported LAUREL-like behavior today

DeltaZulu.Agent's auditd path supports these LAUREL-inspired transformations before KQL runs:

| Transformation | Agent behavior |
| --- | --- |
| Combine multi-line auditd records | Records with the same audit ID are assembled into one source event. |
| Preserve raw evidence | The source event includes `RawEvent` with the original audit lines. |
| Group record types | Record types become nested fields such as `SYSCALL`, `EXECVE`, `PATH`, and `PROCTITLE`; excluded audit record types do not appear. |
| Multi-instance record types | Repeated records such as `PATH` are emitted as arrays. |
| Command-line arguments | `EXECVE.a0`, `EXECVE.a1`, ... are ordered into `EXECVE.ARGV`; `PROCTITLE.proctitle` is split into `PROCTITLE.ARGV`. |
| Hex path readability | Hex-encoded `PATH.name` values are decoded when possible. |
| Source-native filtering | The default Linux auditd profile keeps all assembled auditd events and leaves narrower predicates, such as `execve`, to ad hoc or downstream KQL. |

## Boundary to keep clear

Use Agent KQL for resource-local filtering and investigation. Do not make profile KQL the durable semantic-normalization layer for auditd. Field renames, ECS/ASIM-style mappings, joins with inventory/process/session context, and detection-specific projections belong in server-side/platform KQL, explicit enrichment components, or ad hoc CLI queries.
