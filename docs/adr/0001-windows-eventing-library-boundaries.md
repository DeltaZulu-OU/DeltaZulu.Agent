# ADR 0001: Windows eventing library boundaries

## Status

Accepted.

## Context

DeltaZulu collects and imports several Windows eventing sources. The repository
currently uses three implementation families that overlap but are not equivalent:

- `Microsoft.Diagnostics.Tracing.TraceEvent` for live ETW session ownership,
  provider enablement, kernel provider enablement, and live TraceEvent payload
  access.
- `Tx.Windows` / `Tx.Windows.Logs` for Windows Event Log, EVTX, and ETL import
  paths where replay and TDH-backed event dictionaries are a good fit.
- Direct P/Invoke for small OS capabilities that are not event-stream parsers or
  where using a full eventing library would hide the security or resource-control
  boundary.

The overlap is intentional today. TraceEvent, Tx, and P/Invoke each expose a
different level of control. Replacing one with another should require measured
benefit, not just API consolidation.

## Decision

Use each implementation family at the boundary where it gives the best control,
correctness, and operational clarity.

| Boundary | Use | Why |
|---|---|---|
| Live ETW session ownership and managed-mode provider enablement | `Microsoft.Diagnostics.Tracing.TraceEvent` | It owns real-time sessions, exposes `TraceEventSession`, supports kernel provider enablement, provides `TraceEventProviderOptions`, and lets DeltaZulu control session lifetime, buffer sizing, provider enablement, and shutdown behavior. |
| Attach-mode live ETW consumption | `TraceEventSession` attach path | Keeps live session attachment in the same TraceEvent model as managed mode while preserving RealTimeKql-compatible attach semantics. |
| Live ETW hot-path parsing/materialization | TraceEvent plus DeltaZulu primitives | TraceEvent supplies the event object; DeltaZulu extracts `NativeEtwEnvelope`, applies native identity filters, and materializes selected payload fields so operation labels and enrichment remain product-owned schema. |
| ETL import and offline replay | `Tx.Windows.EtwTdhObservable.FromFiles` | Tx is well-suited to file/replay workflows and TDH-backed dictionaries. Its lazy-materialization and replay concepts should influence optimization without replacing live collection. |
| EVTX import | `Tx.Windows.EvtxObservable.FromFiles` | Tx provides the file import abstraction DeltaZulu needs for deterministic EVTX ingestion without owning the Windows Event Log subscription lifecycle. |
| Windows Event Log XML mapping | .NET / Tx-compatible mapping helpers | DeltaZulu owns field-shaping and named `EventData` extraction while using Windows eventing abstractions for source access. |
| ETW prologue integrity self-check | P/Invoke | The monitor reads current-process memory and resolves `ntdll` exports. This is not event collection; it requires explicit Windows API calls and a narrow security boundary. |
| Windows process resource limits | P/Invoke | Job object assignment is OS resource control, not event ingestion. The daemon keeps this boundary explicit with small native calls. |
| Linux FIFO support | P/Invoke | `mkfifo`/`stat` are small platform primitives outside Windows eventing. |
| Future full native ETW backend | P/Invoke/native ETW APIs only if benchmarks justify it | Native ETW APIs (`StartTrace`, `ControlTrace`, `OpenTrace`, `ProcessTrace`, TDH, `EnableTraceEx2`) provide maximum control but have high implementation and maintenance cost. |

## Rationale

### Why TraceEvent for live ETW?

Live collection needs session lifecycle control, provider enablement, buffer
configuration, attach/create modes, and predictable shutdown. TraceEvent already
models these concerns with `TraceEventSession`. DeltaZulu adds product-specific
hot-path rules around it: native envelope extraction, selected payload
materialization, bounded handoff, resolver provenance, and health counters.

### Why not Tx for live ETW?

Tx is valuable, but replacing TraceEvent for live collection would not solve the
main production concerns: bounded callback handoff, provider/session ownership,
source-side native filtering, selected payload materialization, and DeltaZulu
schema provenance. Tx remains a reference for partition-key dispatch, lazy TDH
materialization, replay, and virtual-time concepts.

### Why Tx for ETL and EVTX?

Offline file ingestion benefits from replay-oriented APIs and TDH-backed field
access. Tx lets DeltaZulu import ETL/EVTX without implementing native file replay
first. The optimization direction is to preserve Tx's lazy behavior by reading
system/native identity fields first and materializing selected payload fields only
when needed.

### Why P/Invoke at all?

P/Invoke is reserved for boundaries that are not well-served by TraceEvent or Tx:
current-process memory reads, module/export resolution, job objects, FIFOs, and a
future native ETW backend if profiling proves TraceEvent is insufficient. Native
calls must stay narrow, wrapped, tested, and documented.

## Capability overlap and simplification opportunities

| Overlap | Current decision | Future simplification test |
|---|---|---|
| TraceEvent and Tx can both consume ETW-like data. | TraceEvent for live sessions; Tx for ETL/replay. | Keep both unless ETL parity can be achieved with TraceEvent or Tx can satisfy live lifecycle/backpressure requirements with less code. |
| TraceEvent and native ETW APIs can both own sessions. | TraceEvent owns sessions now. | Move to native APIs only if benchmarks show material CPU/allocation/loss improvements that outweigh maintenance cost. |
| Tx lazy materialization and DeltaZulu selected materialization overlap conceptually. | Reuse the pattern, not necessarily the library, in live collection. | If one selected-materialization abstraction can support both TraceEvent and Tx ETL paths cleanly, consolidate the DeltaZulu abstraction. |
| Provider/event/opcode filtering exists in profile KQL and native envelope predicates. | Use native envelope predicates before payload reads, then KQL for richer payload/server logic. | Compile only safe, auditable KQL subsets into native predicates; leave complex expressions in KQL. |
| P/Invoke can expose everything but at high cost. | Keep P/Invoke narrow. | Expand only behind interfaces with deterministic tests and Windows validation notes. |

## Consequences

- The live ETW path remains TraceEvent-based for now.
- Tx remains a dependency for ETL/EVTX and a source of replay/lazy-materialization
  patterns.
- P/Invoke remains explicitly scoped to platform primitives and diagnostics.
- Any new Windows eventing feature must state which boundary it belongs to before
  choosing a library.
- Future simplification is benchmark-driven: remove overlap only when an
  alternative reduces code or dependencies without reducing control, provenance,
  or testability.

## Follow-up work

- Compile safe native identity predicates from ETW profiles before payload reads.
- Wire profile-driven selected payload fields through live ETW and ETL import.
- Add bounded callback handoff for live ETW and expose drop/backpressure counters.
- Add deterministic replay benchmarks for ETL and captured NDJSON.
- Reassess TraceEvent versus native ETW only after benchmark evidence exists.
