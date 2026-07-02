# ETW schema boundaries and DeltaZulu enrichment fields

DeltaZulu treats native ETW event data as the source of truth for raw event
facts. The live ETW collector must not imply that a value produced by
correlation, local lookup, path conversion, normalization, or enrichment was
present in the provider payload. Native provider schemas and DeltaZulu-emitted
schemas are documented separately so analysts can distinguish facts from agent
interpretations.

## Field classification

Every output field belongs to exactly one category. Moving a field between
categories requires a schema-version change.

| Category | Meaning | Examples |
|---|---|---|
| Native ETW envelope field | Field from `EVENT_RECORD` / `EVENT_HEADER`, exposed by TraceEvent from the ETW envelope. | `ProviderGuid`, `ProviderName`, `EventId`, `Opcode`, `Version`, `ProcessId`, `ThreadId`, `TimeStamp`. |
| Native ETW payload field | Field directly parsed from the provider payload for that provider, event, and version. | `FileObject`, `IrpPtr`, `TransferSize`, `OpenPath`, `CreateOptions`. |
| DeltaZulu resolver field | Field produced by stateful correlation. | `ResolvedFilePath`, `FileResolutionSource`, `FileResolutionConfidence`. |
| DeltaZulu enrichment field | Field produced by joining with local context or metadata. | `ResolvedProcessImage`, `ResolvedProcessCommandLine`, `ResolvedUser`, `Hashes`, `PeMetadata`. |
| DeltaZulu quality field | Field describing parser, resolver, or pipeline quality. | `ParserStatus`, `FileResolverMiss`, `EnrichmentStatus`, `ResolutionAgeMs`. |
| DeltaZulu normalized field | Field renamed or reshaped for downstream schema consistency. | `TargetFilename`, `ActionType`, `DeviceName`, `InitiatingProcessId`. |


## Native event identity and normalized operations

DeltaZulu preserves native ETW event identity for cheap source-side filtering and
forensic precision. Source-side ETW filters should prefer native numeric identity
fields whenever they are available: `ProviderGuid`, `ProviderName`, `EventId`,
`Opcode`, `Version`, `Keywords`, `Task`, and `Level`.

Readable operation labels are DeltaZulu-derived convenience fields. They answer
how DeltaZulu classifies the event after parsing, not what ETW natively emitted.
Normalized operation fields are `OperationCode`, `OperationName`,
`OperationFamily`, and `OperationNameSource`. `OperationName` must always be
documented as derived schema, for example with `OperationNameSource =
DeltaZulu.FileIoOpcodeMap`.

For FileIO, prefer filters that combine provider identity with the native numeric
operation field exposed by `WindowsEtw.Native`. Validate the field name against
real records before finalizing a profile. Candidate fields, in preference order,
are `Opcode`, `EventDescriptorOpcode`, `EventId`, `EventType`, and
`OperationCode` only when `OperationCode` is a direct pre-filter copy of the
native value. Avoid source-side filtering on `OperationName`,
`ResolvedFilePath`, `FileResolutionSource`, process image, command line, or
detection-category fields because those are resolver/enrichment outputs.

## Current live ETW collector contract

`TraceEventSourceEventMapper` emits a native ETW envelope plus provider payload
fields. It does not resolve filenames, process ancestry, users, hashes, PE
metadata, Win32 paths, or normalized detection fields. The `SourceEvent`
metadata that wraps the event (`SourceType`, `SourceName`, `ParserName`, host,
profile delivery metadata, and related pipeline fields) is DeltaZulu transport
metadata, not ETW provider payload.

The current live ETW collector therefore has two schemas:

1. **Native provider schema read:** the ETW envelope fields exposed by TraceEvent
   plus whatever payload names `data.PayloadNames` exposes for the specific
   provider event/version.
2. **DeltaZulu schema emitted:** the same native fields, wrapped in DeltaZulu
   source metadata. No resolver, enrichment, or normalized identity fields are
   emitted by the live ETW collector unless a future schema extension documents
   them here.

## Naming rules for derived identity fields

DeltaZulu-derived fields use explicit names that make their origin clear. Do not
use a provider-looking name unless the value is a direct provider payload copy.

| Purpose | Use | Avoid |
|---|---|---|
| Direct provider path | `RawFileName`, `RawFilePath`, `OpenPath` | `FileName` without provenance. |
| Resolved file path | `ResolvedFilePath` | `FileName`. |
| Analyst-facing normalized path | `TargetFilename` with `TargetFilenameSource` | `FileObjectPath`. |
| Resolution source | `FileResolutionSource` | `Source`. |
| Resolution confidence | `FileResolutionConfidence` | `Confidence`. |
| Resolver age | `FileResolutionAgeMs` | `Age`. |
| Resolver miss flag | `FileResolverMiss` | `MissingFile`. |
| Process context source | `ProcessResolutionSource` | `ProcessSource`. |

`TargetFilename` may be used as a normalized analyst-facing path only when it is
accompanied by `TargetFilenameSource` or otherwise mapped from a documented
source. If the provider directly supplied the path, use
`TargetFilenameSource = NativePayload`. If DeltaZulu resolved it through a cache,
use `TargetFilenameSource = Resolver`.

## Required provenance metadata

Every derived identity field must carry provenance metadata.

For file paths:

- `ResolvedFilePath`
- `FileResolutionSource`
- `FileResolutionConfidence`
- `FileResolutionAgeMs`
- `FileResolverKey`
- `FileResolverMiss`

For process context:

- `ResolvedProcessImage`
- `ResolvedProcessCommandLine`
- `ProcessResolutionSource`
- `ProcessResolutionConfidence`
- `ProcessResolutionAgeMs`
- `ProcessGenerationKey`

For user context:

- `ResolvedUser`
- `UserResolutionSource`
- `UserResolutionConfidence`
- `SidResolutionStatus`

For DNS/network correlation:

- `ResolvedDnsName`
- `DnsResolutionSource`
- `DnsResolutionConfidence`
- `DnsCorrelationWindowMs`

## Controlled file resolution source values

These values are DeltaZulu schema values; they are not ETW provider fields.

| Value | Meaning |
|---|---|
| `NativePayload` | The current event directly contained the path. |
| `FileIoCreate` | Path came from a classic FileIO create/open event. |
| `FileIoName` | Path came from a classic FileIO name event. |
| `FileIoRundown` | Path came from a rundown event. |
| `KernelFileCreate` | Path came from a modern Kernel-File create/open event. |
| `KernelFileRename` | Path came from a modern Kernel-File rename event. |
| `KernelFileDeletePath` | Path came from a delete-path event. |
| `FileObjectCache` | Path was resolved from a `FileObject` cache. |
| `FileKeyCache` | Path was resolved from a `FileKey` cache. |
| `DelayedCache` | Path was resolved from a stale or delayed cache entry. |
| `Unknown` | No reliable path could be resolved. |

## Controlled resolution confidence values

Resolution confidence describes enrichment reliability. It is not detection
confidence.

| Value | Meaning |
|---|---|
| `High` | Direct payload or fresh lifecycle correlation. |
| `Medium` | Rundown or cache-based correlation with acceptable age. |
| `Low` | Stale cache, post-delete state, rename ambiguity, or weak correlation. |
| `Unknown` | No resolution exists. |

## DiskIO example: native facts versus derived context

Classic DiskIO read/write events may expose fields such as `FileObject`, `Irp`,
`TransferSize`, `ByteOffset`, `IrpFlags`, and timing fields. They do not
natively contain a resolved filename. Tools such as Process Hacker resolve a
filename by looking up the event `FileObject` in a separate hashtable populated
from FileCreate/FileRundown-style lifecycle events and updated on FileDelete.
That filename is resolver-derived context, not a native DiskIO payload field.

DeltaZulu may implement a similar resolver policy, but emitted resolver results
must be documented and nullable. Unlike a UI that may drop unresolved packets,
DeltaZulu should still emit unresolved events because misses expose coverage
gaps, race conditions, unsupported event versions, missing rundown, or resolver
cache failures.

Native-style DiskIO facts:

```text
ProviderGuid
Opcode
ProcessId
ThreadId
FileObject
Irp
TransferSize
ByteOffset
IrpFlags
HighResResponseTime
```

Derived fields that must not be presented as native DiskIO fields:

```text
ResolvedFilePath
TargetFilename
FileResolutionSource
FileResolutionConfidence
ResolvedProcessImage
ResolvedProcessCommandLine
ResolvedUser
Hashes
```

## Schema extension registry

Every non-native field must be registered before a parser or resolver emits it.
Each registry entry documents field name, category, data type, source, native
equivalent, derivation rule, related confidence field, null behavior, version
introduced, and stability.

| Field | Category | Type | Source | Native equivalent | Derivation | Confidence field | Null behavior | Version introduced | Stability |
|---|---|---|---|---|---|---|---|---|---|
| `FileObject` | Native ETW payload field | UInt64/string | ETW payload | `FileObject` | Direct parse. | None | Absent if provider event/version does not expose it. | Native | Stable |
| `ResolvedFilePath` | DeltaZulu resolver field | String/null | File identity resolver | None | Lookup by `FileObject` or `FileKey`. | `FileResolutionConfidence` | Null means unresolved or resolver unsupported. | Future schema extension | Experimental |
| `FileResolutionSource` | DeltaZulu resolver field | Enum | File identity resolver | None | Records resolver path using controlled file resolution source values. | `FileResolutionConfidence` | `Unknown` when unresolved. | Future schema extension | Experimental |
| `TargetFilename` | DeltaZulu normalized field | String/null | Native path or resolver | None | Analyst-facing path from native payload or resolver with `TargetFilenameSource`. | Related source-specific confidence field | Null means no reliable path was available. | Future schema extension | Experimental |
| `ResolvedProcessImage` | DeltaZulu enrichment field | String/null | Process resolver | None for most file events | Join by process generation key. | `ProcessResolutionConfidence` | Null means unresolved or unavailable. | Future schema extension | Experimental |
| `ParserStatus` | DeltaZulu quality field | Enum | Parser | None | Parser result classification. | None | Absent until a parser declares support. | Future schema extension | Experimental |
| `FileResolverMiss` | DeltaZulu quality field | Boolean | File resolver | None | True when a raw file identity exists but no path was found. | `FileResolutionConfidence` | Absent when no file resolver ran. | Future schema extension | Experimental |
| `Irp` | Native ETW payload field | UInt64/string | ETW payload | `IrpPtr` / `Irp` | Direct parse. | None | Absent if provider event/version does not expose it. | Native | Stable |
| `OperationCode` | DeltaZulu normalized field | Integer/null | Parser or opcode map | `Opcode`, `EventId`, or provider event type when directly copied | Numeric operation identity preserved for normalized output. | None | Null when no operation code is available. | Future schema extension | Experimental |
| `OperationName` | DeltaZulu normalized field | String/null | DeltaZulu operation lookup | None | Human-readable lookup from `OperationCode`. | None | Null for unknown operation codes. | Future schema extension | Experimental |
| `OperationNameSource` | DeltaZulu quality field | String/null | DeltaZulu operation lookup | None | Documents which lookup produced `OperationName`. | None | Null when no operation label was produced. | Future schema extension | Experimental |
| `OperationCorrelationSource` | DeltaZulu quality field | Enum | IRP operation tracker | None | Records whether start/end were matched, missing, reused, or unmatchable. | None | `Unknown` when operation state cannot be determined. | Future schema extension | Experimental |
| `OperationDurationMs` | DeltaZulu quality field | Number/null | IRP operation tracker | None | Difference between matched start and end timestamps. | None | Null unless an IRP start/end match exists. | Future schema extension | Experimental |
| `MissingStartEvent` | DeltaZulu quality field | Boolean | IRP operation tracker | None | True when an end has no known start. | None | False for matched or start-only events. | Future schema extension | Experimental |
| `MissingEndEvent` | DeltaZulu quality field | Boolean | IRP operation tracker | None | True when a start has no observed end. | None | False for matched or unmatched-end events. | Future schema extension | Experimental |
| `FileInfoClassName` | DeltaZulu enrichment field | String/null | `FILE_INFORMATION_CLASS` lookup | None | Nullable name for raw `FileInfoClass`. | None | Null for unknown numeric values. | `DeltaZulu.Etw.FileInfoClass/1.0` | Stable |
| `ThreadState` | DeltaZulu enrichment field | String/null | Scheduler context tracker | None | Lookup from CSwitch thread-state code. | None | Null for invalid or unavailable state values. | Future schema extension | Experimental |
| `ThreadWaitReason` | DeltaZulu enrichment field | String/null | Scheduler context tracker | None | Lookup from CSwitch wait-reason code. | None | Null for unsupported or absent wait reasons. | Future schema extension | Experimental |
| `ThreadWaitCategory` | DeltaZulu enrichment field | Enum | Scheduler context tracker | None | Classification of wait reason. | None | `Unknown` for absent or unsupported wait reasons. | Future schema extension | Experimental |
| `ThreadWasInIoWait` | DeltaZulu enrichment field | Boolean | Scheduler context tracker | None | Conservative classification from wait reason. | None | False when wait reason is absent or not I/O-related. | Future schema extension | Experimental |
| `ThreadIdentityStatus` | DeltaZulu quality field | Enum | Scheduler context tracker | None | Marks thread identity as resolved, missing, or anonymized. | None | `Missing` when ETW did not provide a thread ID. | Future schema extension | Experimental |


## Identity-aware ingestion implementation

DeltaZulu keeps ETW callback work minimal: copy native identifiers and payload
fields, enqueue compact records, and perform expensive resolver/enrichment work
outside the callback. The portable resolver primitives live in
`DeltaZulu.Pipeline.Inputs.Etw` so they can be unit-tested without a Windows ETW
session.

Initial implemented primitives are:

- `FileIdentityResolver`, which preserves `FileObject` / `FileKey` raw evidence,
  records lifecycle paths from create/name/rundown-style events, resolves later
  object-only activity, downgrades deleted entries to delayed-cache confidence,
  and emits unresolved results with `FileResolverMiss = true`.
- `ProcessIdentityResolver`, which keys process context by PID plus optional
  generation/start time and returns image/command-line provenance without
  treating PID alone as long-lived identity.
- `ThreadIdentityResolver`, which maps thread IDs to process IDs until a thread
  stop observation removes the mapping.
- `IrpOperationTracker`, which keeps IRP operation lifecycle correlation separate
  from file identity resolution and emits matched, missing-start, missing-end,
  reused-IRP, and without-IRP states.
- `SchedulerContextTracker`, which keeps optional CSwitch/ReadyThread context
  separate from file, IRP, and actor identity. It records thread state, wake
  relationships, wait reasons, I/O-wait classification, anonymized or missing
  thread identity, and per-CPU mismatch diagnostics.
- `FileIoOpcodeLookup`, `FileInfoClassLookup`, `ThreadStateLookup`, and
  `WaitReasonLookup`, which preserve raw numeric values while providing nullable
  readable names and stable category mappings.
- `EtwCollectorMetrics`, which exposes counters for received, parsed, enqueued,
  dropped, lost, parser-failure, resolver-hit/miss, rundown, IRP correlation,
  file-info-class lookup, scheduler context, callback-exception, forwarder-failure,
  and spool-byte telemetry.

These primitives do not replace native ETW fields. They produce DeltaZulu
resolver, enrichment, and quality fields that must remain nullable and
source-attributed in emitted schemas.

## I/O operation lifecycle tracking

File identity and operation identity are separate correlation layers.
`FileObject` and `FileKey` answer which target file object is involved; `Irp`
answers which I/O request start matches an `EndOperation` event. DeltaZulu
therefore keeps three independent correlation layers:

| Layer | Primary key | Purpose |
|---|---|---|
| File identity resolver | `FileObject`, `FileKey` | Resolve target file identity to a path. |
| I/O operation tracker | `Irp` | Match start/end events, duration, and NTSTATUS. |
| Process/thread resolver | `ProcessId`, `ThreadId`, process generation | Resolve execution context, ancestry, image, command line, and user. |
| Scheduler context tracker | `ThreadId`, CPU, timestamp | Track running/waiting/wakeup state and I/O wait context. |

The implemented `IrpOperationTracker` stores active starts by `Irp`, completes
them when a matching end arrives, emits `MissingStart` for unmatched ends, emits
`MissingEnd` when starts are flushed, marks `IrpReused` when a new start reuses
an active IRP, and bounds active operations by flushing the oldest starts as
missing-end correlations.

Normalized operation fields are DeltaZulu quality/correlation fields, not native
FileIO payload fields: `OperationCorrelationSource`, `OperationStartUtc`,
`OperationEndUtc`, `OperationDurationMs`, `NtStatus`, `ExtraInfo`,
`MissingStartEvent`, `MissingEndEvent`, and `IrpReusedBeforeEnd`.

Allowed `OperationCorrelationSource` values are `IrpStartEnd`, `MissingStart`,
`MissingEnd`, `WithoutIrp`, `IrpReused`, and `Unknown`.

## FileIO lookup registries

DeltaZulu records the modern FileIO opcode taxonomy used by the parser layer:

| Opcode | Name |
|---:|---|
| 64 | `CreateFile` |
| 65 | `Cleanup` |
| 66 | `Close` |
| 67 | `ReadFile` |
| 68 | `WriteFile` |
| 69 | `SetInformation` |
| 70 | `DeleteFile` |
| 71 | `RenameFile` |
| 72 | `DirectoryEnumeration` |
| 73 | `Flush` |
| 74 | `QueryFileInformation` |
| 75 | `FilesystemControlEvent` |
| 76 | `EndOperation` |
| 77 | `DirectoryNotification` |
| 79 | `DeletePath` |
| 80 | `RenamePath` |
| 83 | `FltRead` |
| 84 | `FltWrite` |
| 85 | `FltSetInfo` |
| 86 | `FltQueryInfo` |

`FILE_INFORMATION_CLASS` values are preserved as raw `FileInfoClass` integers
and enriched with nullable `FileInfoClassName` values from the versioned
`DeltaZulu.Etw.FileInfoClass/1.0` lookup. Unknown values keep the numeric value
and emit `FileInfoClassName = null`.

## Scheduler context tracking

Scheduler context is an optional enrichment layer. It does not replace FileIO,
DiskIO, file identity resolution, IRP operation tracking, or process/thread
identity. It answers whether the actor thread was running, waiting, woken,
blocked, or waiting for I/O near an operation.

The implemented `SchedulerContextTracker` consumes CSwitch-like state and
ReadyThread-like wake relationships. CSwitch updates per-CPU running state, stores
the previous thread's state and wait reason, starts a running state for the next
thread, and flags mismatched per-CPU previous-thread observations. ReadyThread
records waker/wakee relationships and marks the wakee as `Ready`. Missing and
anonymized ETW thread IDs are modeled explicitly with `ThreadIdentityStatus =
Missing` or `Anonymized`; they are not coerced into normal thread IDs.

Scheduler enrichment fields are DeltaZulu enrichment/quality fields, not native
FileIO or DiskIO payload fields: `ThreadState`, `ThreadWaitReason`,
`ThreadWaitReasonCode`, `ThreadWaitCategory`, `ThreadWasInIoWait`,
`ThreadStateSource`, `ThreadStateAgeMs`, `CpuId`, `PreviousThreadId`,
`NextThreadId`, `WakerThreadId`, `WakeeThreadId`, and
`ThreadIdentityStatus`.

Thread state lookup values are `Initialized`, `Ready`, `Running`, `Standby`,
`Terminated`, `Waiting`, `Transition`, and `DeferredReady` for raw state values
`0x00` through `0x07`. Unsupported values preserve the raw state code and return
a null name.

Wait reason lookup includes `PageIn` and `WrExecutive` through `WrRundown`. The
first implementation uses a conservative I/O-wait boolean: `PageIn` and `Wr*`
alertable waits in that range are classified as `ThreadWasInIoWait = true`, while
known synchronization, memory, delay, and unknown waits keep their raw reason and
nullable name.

## Hot-path materialization rules

DeltaZulu keeps TraceEvent for live ETW session ownership. The hot path should do
less work per event: copy the native envelope, apply native identity filters,
decode only selected payload fields, enqueue bounded work, and measure drops and
materialization failures. Tx remains a useful reference for offline ETL replay,
partition-key dispatch, and lazy materialization, but it is not replacing the
live TraceEvent collector.

`NativeEtwEnvelope` carries the native event identity used for cheap filtering
and forensic alignment. `NativeEtwIdentityFilter` can reject events by provider,
event ID, opcode, version, and keywords before payload reads. The
TraceEvent payload materializer supports selected payload field names so profile
selection can avoid full dynamic payload expansion.

## Kernel-File pointer-sized semantic decoding

`Microsoft-Windows-Kernel-File` manifest fields such as `Irp`, `FileObject`,
`FileKey`, and `ExtraInformation` can be declared as `win:Pointer`. DeltaZulu
treats these as pointer-sized unsigned values, not dereferenceable addresses. The
raw value is evidence, the hex value is forensic readability, and any decoded
value is DeltaZulu schema enrichment.

Kernel-File semantic decoding follows these rules:

- Preserve raw decimal and stable 16-digit hex values for pointer-sized fields.
- Classify by provider GUID, event ID, version, task/event name, field name, and
  nearby fields such as `InfoClass`, `Status`, and matched start operation.
- Resolve `Irp`, `FileObject`, and `FileKey` through ETW event relationships; do
  not dereference them.
- Decode scalar values through explicit lookup tables such as NTSTATUS,
  `FILE_INFORMATION_CLASS`, and `IO_REPARSE_TAG_*`.
- Treat unknown context as `RawOnly`, not as a parser failure.

The manifest `Microsoft-Windows-Kernel-File` event ID space is distinct from the
classic FileIO opcode-style range. Kernel-File uses provider GUID
`edd08927-9cc4-4e65-b970-c2560fb5c289` and manifest event IDs such as `12 =
Create`, `15 = Read`, `16 = Write`, `24 = OperationEnd`, `26 = DeletePath`, and
`27 = RenamePath`. Classic FileIO operation values such as `64 = CreateFile`,
`67 = ReadFile`, `68 = WriteFile`, and `76 = EndOperation` must not be mixed into
Kernel-File source filters unless explicitly normalized into separate fields.

Initial decoder output fields include `IrpHex`, `FileObjectHex`, `FileKeyHex`,
`ExtraInformationRaw`, `ExtraInformationHex`, `ExtraInformationKind`,
`ExtraInformationDecoded`, `ExtraInformationDecodeStatus`, and
`ExtraInformationDecoder`. These are semantic decoding fields; they are not native
provider payload fields and they never imply memory was read at the pointer-sized
value.

## Session health and forensic alignment

ETW session health monitoring is a diagnostic feature for DeltaZulu-owned
sessions. It uses supported ETW APIs where possible to report expected and
observed provider enablement, keyword/level configuration, collection mode, loss
counters, parser failures, and profile/filter/parser provenance. It does not walk
undocumented kernel memory structures or process handle tables.

`EtwSessionHealthSnapshot` models periodic `EtwSessionHealth` events.
`EtwForensicAlignmentMetadata` models the native event identity and provenance
needed to reconcile DeltaZulu logs with ETL files or forensic ETW evidence
recovered later: host, timestamps, QPC timestamp, provider identity, event
identity, process/thread IDs, activity IDs, session/profile/parser versions,
schema version, and raw payload hash.

Memory-forensic ETW provider/consumer correlation and ETL-buffer recovery remain
out of scope for the live agent and belong in external forensic tooling.

## Design rule

Native ETW fields are facts. DeltaZulu fields are interpretations, joins, or
normalizations. Both are valuable, but they must remain distinguishable.
