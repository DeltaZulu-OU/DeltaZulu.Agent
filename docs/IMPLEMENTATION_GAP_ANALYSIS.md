# Implementation gap analysis

This document compares the current codebase to the accepted target architecture
in `ARCHITECTURE.md`, `ROADMAP.md`, and ADRs 0005-0013. It is intentionally not a
new target design. It is the working inventory of places where the code still
differs from the documents, so migration work can fix real conflicts before the
next architecture phase claims completion.

## Snapshot date

2026-07-19. Revised the same day to reflect ADR 0011 (DeltaZulu.Forward
transport), ADR 0012 (Proton intermediate-protocol ingestion), and ADR 0013
(the `DeltaZulu.Normalize` → `DeltaZulu.Parse` rename), which the mechanical
rename in this revision has already applied to the assembly itself.

## Method

The review inspected the repository code and tests, then compared observable
implementation facts against the roadmap acceptance gates. The primary commands
used were:

```bash
rg --files src tests
rg "TextInputRecord|StructuredInputRecord|ExecutionPlanCompiler|ParsedEventEnvelope|Avro|Arrow|SchemaRegistry|type-contract catalog|ChannelOutputMultiplexer|LightweightSyslogParser|AuditdRecordParser|Ndjson|DeliveryBatch|MessagePack" src tests docs -n
sed -n '1,220p' src/DeltaZulu.Pipeline/DeltaZulu.Pipeline.csproj
sed -n '1,240p' src/DeltaZulu.Agent.Runtime/AgentRuntime.cs
sed -n '1,220p' src/DeltaZulu.Pipeline/Core/ResourcePipeline.cs
sed -n '1,220p' src/DeltaZulu.Pipeline/Outputs/Relp/BufferedRelpSink.cs
sed -n '1,200p' src/DeltaZulu.LocalStream/LocalStreamTopics.cs
sed -n '1,80p' src/DeltaZulu.Parse/ParseAssemblyMarker.cs
```

## Executive summary

The code matches the documented **Phase 1 scaffold** but not the target runtime.
The largest current conflict is not that the code is incomplete; the roadmap
already says that. The risk is that several documents describe target-state
nouns (`ParsedEventEnvelope`, the type-contract catalog, Avro, Arrow, unified
PDAG, LocalStream host, DeltaZulu.Forward, the Proton Kafka-API intermediate)
that do not yet have corresponding production code. Phase 2 has introduced
strict input-record and acquisition-plan scaffolds, but current ingestion is
still profile-centric, `SourceEvent`-based, and output-sink oriented until
adapters and runtime wiring move to those contracts.

The important implementation facts are:

- `DeltaZulu.Pipeline` references `DeltaZulu.Parse` (renamed from
  `DeltaZulu.Normalize` by ADR 0013) and `DeltaZulu.LocalStream`, but both are
  marker/scaffold assemblies.
- `DeltaZulu.Pipeline` still directly references `DeltaZulu.DurableBuffer` and
  creates a DurableBuffer-backed RELP sink.
- `AgentRuntime` still starts one `ResourcePipeline` per `ProfileBinding` and
  uses `ChannelOutputMultiplexer` for multi-profile output serialization.
- Syslog and auditd adapters still parse application-specific plaintext inside
  input adapters.
- Current external and diagnostic output remains NDJSON or MessagePack
  `DeliveryBatch` over literal RELP; there is no Avro, Arrow, type-contract
  catalog, generated DDL, DeltaZulu.Forward framing, Proton Kafka-API
  intermediate, or DuckDB backend mapping code (ADR 0010/0011/0012 remain
  target design only).
- `TextInputRecord`, `StructuredInputRecord`, and `ExecutionPlanCompiler` now
  exist as Phase 2 scaffolds, but current inputs have not been adapted to emit
  them yet.
- Current KQL execution operates over `SourceEvent.ToKqlRow()` dictionaries, not
  over typed catalog-backed `ParsedEventEnvelope` rows.

## Gap matrix

| Area | Documented target | Current code fact | Status | Required fix before claiming target |
| --- | --- | --- | --- | --- |
| Pipeline assembly boundary | One `DeltaZulu.Pipeline` assembly with internal folders; Pipeline may reference Parse, LocalStream, RELP, deterministic format libraries, and approved eventing libraries. | `DeltaZulu.Pipeline.csproj` is multi-targeted and references Parse, LocalStream, RELP, and DurableBuffer. Tests explicitly allow Parse/LocalStream and track DurableBuffer as transitional. | Aligned with Phase 1; transitional DurableBuffer remains. | Keep dependency guards. Remove direct DurableBuffer only when LocalStream-backed forwarding replaces it. |
| Parse | Parse is the sole structural plaintext parser; compatible rules compile into one PDAG per parser domain. | `DeltaZulu.Parse` contains only `ParseAssemblyMarker`; syslog/auditd parsing still occurs in input-specific parser classes. | Gap. | Implement `parse.query`, parser domains/generations, PDAG compiler/runtime, topic-tag validation, and compatibility materializer. |
| Input boundary | Inputs acquire, frame, decode, and emit `TextInputRecord` or `StructuredInputRecord`; they do not parse application-specific plaintext. | `TextInputRecord`, `StructuredInputRecord`, and `ExecutionPlanCompiler` exist. Current inputs still implement `ISourceInput` and emit `SourceEvent`; runtime does not consume acquisition plans. | Partial; roadmap Phase 2 remains active. | Adapt current inputs through compatibility shims, preserve source metadata, and wire plan compilation into runtime validation without replacing execution prematurely. |
| Syslog admission/framing | Syslog adapters perform transport/framing/admission only; valid unknown syslog reaches Parse. | `TcpSyslogInput`, FIFO, and file-tail syslog inputs instantiate `LightweightSyslogParser` and parse directly; TCP reads newline-delimited text and does not implement bounded RFC 6587 octet-counted framing. | Gap. | Split syslog transport/framing/admission from Parse; add size/PRI/admission metrics and RFC 6587 support. |
| Auditd materialization | Auditd correlation runs after Parse and consumes parsed fields. | `AuditdFileInput` directly uses `AuditdRecordParser` and `AuditdEventAssembler` inside the input adapter. | Gap. | Move record parsing to Parse compatibility rules and move multi-record assembly to post-materialization assembly. |
| Runtime topology | Execution plans open each physical resource once, then publish parsed envelopes to LocalStream. | Acquisition plans can be compiled, but `AgentRuntime` still creates one `ResourcePipeline` per `ProfileBinding`; `ResourcePipeline` directly connects input observable to profile executor to output writer. | Gap. | Replace profile-centric runtime with execution-plan compiler/runtime and LocalStream publishers/subscriptions. |
| Output multiplexer | No daemon-level general-purpose output multiplexer in target runtime. | `AgentRuntime.RunMultiple` constructs `ChannelOutputMultiplexer`; tests cover that behavior. | Intentional transition. | Delete or restrict `ChannelOutputMultiplexer` only after `AgentRuntime` no longer starts one pipeline per binding. |
| LocalStream | One LocalStream host owns `agent.parsed` and `agent.output`, with append/read/commit/replay. | `DeltaZulu.LocalStream` exposes only canonical topic constants. | Gap. | Implement host, storage, producers, subscriptions, commit/replay, expiry, metrics, and failure tests. |
| Forwarding durability | Forwarder subscribes to `agent.output` and commits only after ACK. | `BufferedRelpSink` owns a `DurableBufferHost<DeliveryRecord>` directly and starts a `RelpOutputWorker`. | Intentional transition. | Move DurableBuffer behind LocalStream, make the forwarder an ACK-gated stream subscription, then remove Pipeline's direct DurableBuffer reference (ROADMAP.md Phase 12). |
| Forward transport | DeltaZulu.Forward (ADR 0011): a proprietary, RELP-derived but non-wire-compatible framing protocol, implemented in `DeltaZulu.Pipeline`, carries Avro batches with ack-on-commit, a typed handshake, and a collector-side dedup window. | No `DeltaZulu.Forward` code exists. Current forwarding speaks literal RELP via the external `DeltaZulu.Relp` submodule and sends MessagePack `DeliveryBatch` payloads, not Avro. | Gap introduced by ADR 0011; target only. | Implement binary framing, handshake, dedup window, and the protocol state machine (ROADMAP.md Phase 12a) with its own test harness before retiring RELP as the primary transport. |
| Typed envelopes | Parsed events carry stable identity, materialization state, catalog logical type metadata, topic, raw message, and provenance. | Current row model is `SourceEvent` and `ResourceOutputRecord`; `SourceEvent.ToKqlRow()` returns a dictionary plus `_metadata`. | Gap. | Add `ParsedEventEnvelope` and materialization outcome model, then adapt existing filters through compatibility projection. |
| Type-contract catalog and type fidelity | Catalog generates Avro, Arrow, sink DDL, translator type tables, and governed JSON projections. | No catalog, Avro, Arrow, Proton, DuckDB, generated DDL, or translator mapping implementation was found. | Gap introduced by ADR 0010; target only. | Define catalog model/generation lifecycle, schema projections, validation failure handling, and per-backend mappings before claiming sink parity. |
| Internal wire format | Avro on the agent-to-collector wire (carried by DeltaZulu.Forward), decoded once to Arrow batches on the collector. | Current daemon output choices are console/file NDJSON and RELP; RELP sends MessagePack `DeliveryBatch`. | Gap; existing docs must call NDJSON/MessagePack transitional where used internally. | Keep NDJSON/MessagePack/RELP paths as current compatibility mechanisms until Avro wire, DeltaZulu.Forward, and Arrow collector batches exist; do not describe them as target type-bearing transport. |
| Proton ingestion mechanism | No bespoke Proton sink; the collector publishes typed Avro batches to a Kafka-API-compatible intermediate (Redpanda/embedded broker), with a Python external-stream plugin as fallback (ADR 0012). | No Proton-leg publishing code, Kafka producer wiring, or Python fallback plugin exists in this repository. | Gap introduced by ADR 0012; target only. | Add collector-side Kafka producer wiring, catalog→topic/schema mapping, Proton external-stream DDL projection, and the Python fallback plugin (ROADMAP.md Phase 3b/18). |
| KQL execution parity | One KQL query should translate against Proton and DuckDB from a shared physical type catalog. | Current filter execution is Rx.Kql over in-memory dictionaries. No Proton/DuckDB query translator code was found in this repository path. | Gap / possibly out-of-repo. | Document ownership if backend translator lives elsewhere; otherwise add type-catalog-driven translator work to the server roadmap before asserting parity. |
| Observability/blindness | Admission, parser, filter, LocalStream, forwarding, and unknown-event diagnostics are separately observable. | Observation classes and accumulators exist, but there is no Parse/LocalStream path to emit the full target taxonomy. | Partial. | Add bounded labels and separate metrics at each new boundary as the boundaries are implemented. |

## Documentation conflicts and guardrails

1. **Phase-number drift has been corrected in touched scaffold comments.**
   `LocalStreamTopics`, `ParseAssemblyMarker`, and the transitional
   DurableBuffer project-reference comment now point to the current roadmap phase
   numbers. Keep code comments aligned whenever phases are renumbered, because
   comments are executable guidance for maintainers.
2. **Architecture diagrams show target nouns without enough current-state guard.**
   `ARCHITECTURE.md` is intentionally target-state, but nearby roadmap language
   must continue to state that the type-contract catalog, Avro, Arrow,
   DeltaZulu.Forward, the Proton Kafka-API intermediate, LocalStream host,
   parsed envelopes, and execution plans are not implemented yet.
3. **NDJSON appears in both target-edge and current-internal roles.** The current
   code still uses NDJSON sinks and MessagePack RELP batches. Documents should
   consistently label these as current/transitional internal mechanisms or
   governed edge mechanisms, not as the target type-bearing transport.
4. **KQL/backend parity is a target assertion without in-repo backend code.** The
   docs should avoid implying Proton/DuckDB parity is present until the catalog,
   generated DDL, and translator mappings exist and are tested.
5. **RELP's scope narrowed, not removed, by ADR 0011.** `DeltaZulu.Relp` remains
   the accurate description of the current transitional transport (ADR 0006)
   and of any future rsyslog-world peer input adapter; it is no longer the
   description of the target production agent-to-collector transport, which is
   DeltaZulu.Forward. Documents referencing "the RELP forwarder" as target
   architecture should be corrected to describe the transitional-vs-target
   split explicitly.

## Immediate documentation follow-up

Before implementing the next architecture phase, keep the roadmap baseline tied
to this gap analysis. Future PRs should update this document when they close a
gap, add a new accepted deviation, or discover that backend work lives outside
this repository. The next known Phase 2 fix is adapting concrete inputs to emit
strict records through compatibility shims while the legacy `SourceEvent` runtime
continues to run. DeltaZulu.Forward (Phase 12a) and the Proton Kafka-API
intermediate (Phase 3b/18) are newly tracked gaps from ADR 0011/0012 and have no
implementation yet; do not begin coding them speculatively ahead of their
prerequisite phases (LocalStream host for 12a; the type-contract catalog and
Avro wire for 3b).
