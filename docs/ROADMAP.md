# Architecture migration roadmap

This roadmap tracks migration to the target topology in
[`ARCHITECTURE.md`](ARCHITECTURE.md). It replaces earlier extraction-first,
DurableBuffer-first, profile-per-source, and permanent-multiplexer plans.

## Status legend

- **Complete**: target decision/documentation is recorded; no implementation
  claim is implied.
- **Active**: the current implementation focus; its completion evidence has not
  yet been met.
- **Planned**: accepted work not yet implemented.
- **Transitional**: current code intentionally differs while a later phase is
  pending.

## Current baseline

The repository has one multi-targeted `DeltaZulu.Pipeline` assembly that
references `DeltaZulu.DurableBuffer` and `DeltaZulu.Relp` directly, plus the
`DeltaZulu.Parse` (ADR 0013; renamed from `DeltaZulu.Normalize`) and
`DeltaZulu.LocalStream` scaffold assemblies added in Phase 1 (marker types
only; the PDAG compiler and stream runtime are Phase 6-7 and Phase 9 work).
The daemon still runs separate `ProfileBinding`/`ResourcePipeline` work and
uses `ChannelOutputMultiplexer` to serialize concurrent legacy output. Those
are transitional implementation details, not target architecture. Existing
syslog and auditd parsing is also transitional until Parse parity is
established. Agent-to-collector forwarding still speaks literal RELP
(`DeltaZulu.Relp`); DeltaZulu.Forward (ADR 0011), the target transport, is not
yet implemented.

## Current delivery status (2026-07-17)

**We have completed the architectural foundation (Phases 0 and 1) and are now
working on Phase 2: input contracts.** `TextInputRecord`,
`StructuredInputRecord`, and an initial `ExecutionPlanCompiler` now exist as
Phase 2 scaffolds. The next implementation step is to adapt existing inputs
without losing their current source metadata. This establishes
the strict source boundary required before Phase 6 can add `parse.query` and
Phase 7 can compile a unified PDAG.

The following capabilities are deliberately **not** claimed as present yet; the
current code/documentation gap inventory is maintained in
[`IMPLEMENTATION_GAP_ANALYSIS.md`](IMPLEMENTATION_GAP_ANALYSIS.md).

- Parse is not a plaintext parser yet; it is an assembly boundary only.
- LocalStream has no host, storage, producer, subscription, replay, or commit
  implementation yet; `agent.parsed` and `agent.output` are canonical names
  only.
- The daemon is not yet an execution-plan runtime and still uses per-profile
  `ResourcePipeline` instances, direct DurableBuffer forwarding, and
  `ChannelOutputMultiplexer`.
- The type-contract catalog, Avro wire schemas, Arrow record batches, generated
  Proton/DuckDB DDL, and translator type tables are accepted target design only;
  no implementation exists in this repository yet.
- DeltaZulu.Forward (ADR 0011) and the Proton Kafka-API-compatible intermediate
  (ADR 0012) are accepted target design only; current forwarding still speaks
  literal RELP and no Proton-leg publishing code exists.

Phase 2 work must preserve the legacy behavior through compatibility adapters;
it must not prematurely move structured sources through Parse or replace
the daemon runtime. The type-contract-catalog/Avro/Arrow architecture recorded
in ADR 0010 is accepted target design, but it is not implemented in the current
baseline and must not be implied by Phase 2 input records alone. Parse is
the first production migration priority after the typed input boundary: it
establishes the parsed-event contract that LocalStream will persist. LocalStream
design and contract work may proceed in parallel, but direct DurableBuffer replacement must not make LocalStream
authoritative for legacy profile-specific `SourceEvent` records.

## Ordered migration

| Phase | Status | Objective | Completion evidence |
| --- | --- | --- | --- |
| 0 | Complete | Align ADRs and authoritative documentation. | Architecture, roadmap, README, and ADRs state one Pipeline, LocalStream boundaries, PDAG, RELP ownership, and no production multiplexer. |
| 1 | Complete | Add Parse/LocalStream and architecture guards. | Pipeline references Parse, LocalStream, and RELP (`DeltaZulu.Pipeline.csproj`); `PipelineAssembly_ReferencesOnlyExternalPipelineDependencies`/`PipelineAssembly_ReferencesParseAndLocalStream` reject Agent-layer references and `PipelineAssembly_TransitionalDirectDurableBufferReferenceIsTracked` reports the direct DurableBuffer use in `tests/DeltaZulu.Agent.Tests/ApplicationTests.cs` (and the equivalent in `DomainTests.cs`). Both new assemblies are scaffolds: they exist as real, referenced, tested project boundaries but implement no PDAG or stream runtime yet, which remain later phases. |
| 2 | Active | Define strict input contracts and compile validated acquisition plans. | `TextInputRecord` and `StructuredInputRecord` preserve acquisition metadata; resource configuration separates kind, framing, payload format, admission, parser domain, and deterministic acquisition key. The initial `ExecutionPlanCompiler` normalizes and rejects conflicting physical-resource definitions without replacing runtime execution. |
| 3 | Planned | Generalize acquisition, framing, and decoding through protocol-specific adapters. | `file`, `fifo`, `syslog-tcp`, `syslog-udp`, and `syslog-relp` adapters emit typed records with bounded framing and no application parser dependency. FIFO creation/reopen is explicit configuration, not a syslog behavior. |
| 4 | Planned | Add syslog admission presets. | TCP and UDP may use the same numeric port; TCP supports bounded RFC 6587 octet-counted and newline framing; PRI validation, decoding, size checks, and rejection metrics run before Parse. Valid unknown syslog reaches Parse. |
| 5 | Planned | Move structured sources and RELP payload adapters to structured contracts. | CSV, Windows, and MessagePack `DeliveryBatch` sources bypass Parse; RELP protocol handling remains framing/session work and payload type selects text versus structured materialization. |
| 6 | Planned | Add restricted `parse.query` and a Parse compatibility materializer. | Existing `filter.query` profiles continue receiving compatible `SourceEvent` shapes; profile-scoped diagnostics validate topic-tagged parser rules; raw text and parser provenance are retained. |
| 7 | Planned | Build unified Parse PDAG generations. | Parser domains group compatible rules deterministically; each plaintext record traverses one PDAG; recognized, unrecognized, and error outcomes remain explicit. |
| 8 | Planned | Move auditd correlation after Parse. | The assembler consumes parsed fields, maintains bounded incomplete-group state, and retires the hardcoded audit parser in the acquisition adapter. |
| 9 | Planned | Define parsed envelopes, type-contract catalog entries, and implement the LocalStream host. | `ParsedEventEnvelope` has stable identity, materialization state, and catalog-backed logical type metadata; Avro wire schemas, Arrow server schemas, generated sink DDL, and translator type tables are derived from one catalog generation. One LocalStream host provides append/read/commit/replay for `agent.parsed` and `agent.output`, but legacy DurableBuffer remains the active forwarder until dispatch and ACK semantics are proven. |
| 10 | Planned | Replace profile-centric execution with execution plans. | The planner opens each physical resource once per acquisition key and binds acquisition to parser/materialization/stream plans deterministically; `ProfileBinding` no longer defines a pipeline instance. |
| 11 | Planned | Add coordinated filter dispatch. | Every candidate runs in deterministic order; output append precedes parsed commit; no-candidate, no-match, filter error, and output error remain distinct. |
| 12 | Planned | Migrate forwarding to LocalStream `agent.output` and retire direct DurableBuffer ownership. | The forwarder is an ACK-gated LocalStream subscription with replay; no forwarder-created DurableBuffer host remains Agent-visible. This phase may land with the transitional RELP transport before DeltaZulu.Forward (Phase 12a) replaces it. |
| 12a | Planned | Implement DeltaZulu.Forward (ADR 0011) as the target agent-to-collector transport. | Binary framing (type, txnr, length, flags), typed offer/capability handshake (catalog version, schema fingerprints, compression, dedup-window size), one Avro batch per frame with ack-on-durable-commit, batch UUIDs, collector-side bounded dedup window, and backpressure/window-adjustment frames all exist as an independently tested state machine (retransmit-after-reconnect races, cross-session duplicates, txnr wraparound, half-open detection, window exhaustion, shutdown with unacked frames) per its own harness budget. `DeltaZulu.Relp`/literal RELP is retired as the primary transport once Forward is proven; it may remain for a separate rsyslog-world peer input adapter. |
| 13 | Planned | Remove daemon multiplexer. | The daemon does not instantiate `ChannelOutputMultiplexer`; lifecycle uses plan-owned tasks, stream drain policy, and only private publisher serialization where required. |
| 14 | Planned | Remove compatibility plaintext parsers. | Parse-only plaintext parsing has syslog, journal/FIFO, and auditd parity corpora; no transport adapter invokes an application-specific parser. |
| 15 | Planned | Add blindness and end-to-end observability. | Admission, parser, filter, complete-blindness, streams, forwarding, and bounded unknown diagnostics are observable. |
| 16 | Planned | Harden reload and operations. | Parser and filter generations replace atomically; checkpoints, poison policy, retention/storage pressure, graceful drain, and failure injection are covered. |
| 17 | Planned | Archive conflicting operational guidance. | All active examples and commands validate; historical documents are marked superseded. |
| 18 | Planned | Verify sink ingest and replay assumptions. | Proton's Kafka-API-compatible external-stream ingestion is wired per ADR 0012 (Redpanda/embedded Kafka-protocol broker primary path, Python external-stream fallback) and its logical-type handling is verified against the targeted OSS version — the availability question is closed affirmatively (ADR 0012); only the logical-type declaration form remains open. Arrow-to-DuckDB appender performance is benchmarked against NDJSON at realistic event rates; agent Avro spooling/replay ordering and schema-version pinning are specified and tested; broker-hop latency is measured against the NRT budget (ADR 0012 revisit trigger). |
| 19 | Planned | Optimize only with benchmarks. | Before/after evidence preserves parsing, commit, queue, and blindness invariants. |

Phases 2–5 establish the strict source boundary. Phases 6–8 are the first
production migration priority because they remove parser ownership overlap and
stabilize materialization semantics. LocalStream contract/design work may run
alongside them, but Phase 12 cannot begin until parsed envelopes, execution
plans, coordinated dispatch, and commit-order tests exist. Phases 9–13 are one
coordinated daemon and type-contract migration and must not be represented as
final architecture until completed.

## Acceptance gates

### Parsing and profiles

- Compatible plaintext rules compile into one deterministic PDAG per domain.
- Every rule has exactly one `topic.*` tag; full `event.tags` remains queryable.
- Structured sources bypass Parse; valid unknown plaintext is preserved.
- `parse.query` is optional and separate from Rx.Kql `filter.query`; no profile
  exposes streams, subscriptions, offsets, partitions, generations, or batching.
- A source specification names a transport/acquisition kind separately from
  framing, payload format, admission policy, and Parse parser domain. The
  planner rejects incompatible combinations and conflicting acquisition keys.

### Durability and dispatch

- One LocalStream host owns `agent.parsed` and `agent.output`, initially with one
  partition each and bounded retention.
- The type-contract catalog is the sole type authority for parsed fields and
  generates Avro schemas, Arrow schemas, Proton/DuckDB DDL, translator type
  mappings, and governed JSON projections.
- Internal type-bearing transport is Avro to the collector (over
  DeltaZulu.Forward per ADR 0011 once implemented) and Arrow in collector
  memory; NDJSON is limited to third-party ingress/egress, debug taps, and
  dead-letter/error envelopes.
- Parsed positions commit only after all output appends succeed or a recorded
  successful zero-output disposition; output positions commit only after a
  forwarding acknowledgement (RELP today; a DeltaZulu.Forward batch ack once
  Phase 12a lands).
- Logical topics remain envelope properties. No `parsed.sshd`-style streams and
  no general-purpose daemon multiplexer are introduced.

### Runtime and coverage

- A physical resource opens once per deterministic acquisition key.
- Input adapters emit collection facts and payloads only; they never invoke
  application-specific syslog, journal, or auditd parsers.
- Parser and filter generations replace atomically and independently.
- Admission rejection, parser no-match, filter no-candidate, filter no-match,
  and operational errors remain distinct. Unknown records never disappear
  silently.
- UTC microsecond event time, explicit duration units, large integer/decimal
  fidelity, UUID/IP/MAC annotations, and null/absent/empty-string semantics are
  tested through both Proton and DuckDB mappings.

## Type-fidelity migration notes

ADR 0010 adds a type-fidelity track to the migration. The type-contract
catalog is producer-agnostic: liblognorm-derived parser output, direct
XML/CSV/JSON converters, native Windows sources, and future structured inputs
all converge on the same catalog. This prevents a later retrofit where field
types are implicitly tied to Parse rules.

NDJSON remains useful as an edge dialect, but it is no longer the target
internal type-bearing transport. Agents cache schemas and spool Avro; they fail
visibly on schema rejection instead of falling back to NDJSON. The collector
decodes Avro once into Arrow record batches, then fans out to DuckDB
zero-copy and to Proton through a Kafka-API-compatible intermediate protocol
per ADR 0012 — not a bespoke sink. Proton's Kafka-API external-stream support
is verified as available in the target OSS version (ADR 0012); whether its
Avro handling honors catalog logical types directly or needs explicit column
declaration is a Phase 3b/18 integration-testing question.

DeltaZulu.Forward (ADR 0011) is the target Avro-carrying transport between
agent and collector, replacing literal RELP once Phase 12a lands; see ADR
0011 for the framing, handshake, and dedup-window design and ADR 0006 for the
narrowed, transitional role RELP retains.

## Validation expectations

Run the repository's relevant .NET restore/build/test commands after each phase.
Build both `net10.0` and `net10.0-windows` in CI. Windows source changes also
need Windows-host validation. New topology work requires deterministic replay,
commit-order, reload, and failure-injection coverage before it is called complete.

## Historical guidance

The legacy direct DurableBuffer forwarding path, profile-per-source daemon
execution, hardcoded syslog/auditd plaintext parsers, and
`ChannelOutputMultiplexer` are retained only as transitional descriptions of the
current baseline. They are not design options for new daemon work.

## Phase 10, 12, and 13 implementation notes

Concrete anchors for the two phases that retire the current profile-per-source
runtime, so migration work has file-level targets in addition to the
architecture-level objective above.

**Phase 10 — execution plans replace `ProfileBinding`/`ResourcePipeline`:** the
current legacy code is `src/DeltaZulu.Agent.Runtime/AgentRuntime.cs`
(`RunSingle`/`RunMultiple`/`RunBinding`), `ProfileBinding.cs`, and
`src/DeltaZulu.Pipeline/Core/ResourcePipeline.cs`. `AgentRuntime.RunMultiple`
(`AgentRuntime.cs:109`) is the exact place that currently starts one
`ResourcePipeline` per `ProfileBinding`; this phase replaces that with
acquisition/parser/filter plan binding.

**Phase 12 — replace direct DurableBuffer forwarding:** the current
`BufferedRelpSink` owns the DurableBuffer host, starts the forwarding worker,
and drains it during shutdown. It is not replaced merely by adding LocalStream:
the LocalStream-backed forwarder must subscribe to `agent.output`, replay after
restart, and commit only after a delivery acknowledgement (RELP until Phase
12a). The direct DurableBuffer project reference is removed only after those
behaviors and their failure tests are in place. Phase 12a then replaces the
RELP acknowledgement with a DeltaZulu.Forward batch acknowledgement per ADR
0011; Phase 12 does not require Forward to exist first.

**Phase 13 — remove `ChannelOutputMultiplexer`:** the deletion condition is
`AgentRuntime` no longer starting one `ResourcePipeline` per `ProfileBinding`
(i.e., Phase 10 complete). Until then, `ChannelOutputMultiplexer`
(`src/DeltaZulu.Pipeline/Core/ChannelOutputMultiplexer.cs`) remains required and
should carry a deletion-condition comment referencing this row.
`CompletionTrackingWriter` (`src/DeltaZulu.Pipeline/Core/CompletionTrackingWriter.cs`)
is a separate, smaller concern and may remain for finite CLI/test workflows
after this phase; it is not part of the daemon's target lifecycle (see
[`ARCHITECTURE.md`](ARCHITECTURE.md), "Lifecycle and reload"). If `dzagentctl`
or `DeltaZulu.Agent.ProfileWorkbench` still need to serialize concurrent writes
to one console/file writer afterward, introduce a narrowly scoped
`SerializedOutputWriter` (serialize-only; no routing, buffering, or profile
semantics) rather than retaining the multiplexer for that purpose.
Multiplexer/completion-writer unit tests currently live in
`tests/DeltaZulu.Agent.Tests/ApplicationTests.cs` (`ChannelOutputMultiplexer_*`,
`CompletionTrackingWriter_*`); this phase removes only the former test group.
