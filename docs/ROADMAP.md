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
`DeltaZulu.Normalize` and `DeltaZulu.LocalStream` scaffold assemblies added in
Phase 1 (marker types only; the PDAG compiler and stream runtime are Phase 6-7
and Phase 9 work). The daemon still runs separate
`ProfileBinding`/`ResourcePipeline` work and uses `ChannelOutputMultiplexer` to
serialize concurrent legacy output. Those are transitional implementation
details, not target architecture. Existing syslog and auditd parsing is also
transitional until Normalize parity is established.

## Current delivery status (2026-07-17)

**We have completed the architectural foundation (Phases 0 and 1) and are now
working on Phase 2: input contracts.** The immediately planned implementation
is to introduce `TextInputRecord` and `StructuredInputRecord`, then adapt
existing inputs without losing their current source metadata. This establishes
the strict source boundary required before Phase 6 can add `parse.query` and
Phase 7 can compile a unified PDAG.

The following capabilities are deliberately **not** claimed as present yet:

- Normalize is not a plaintext parser yet; it is an assembly boundary only.
- LocalStream has no host, storage, producer, subscription, replay, or commit
  implementation yet; `agent.parsed` and `agent.output` are canonical names
  only.
- The daemon is not yet an execution-plan runtime and still uses per-profile
  `ResourcePipeline` instances, direct DurableBuffer forwarding, and
  `ChannelOutputMultiplexer`.

Phase 2 work must preserve the legacy behavior through compatibility adapters;
it must not prematurely move structured sources through Normalize or replace
the daemon runtime. Normalize is the first production migration priority after
the typed input boundary: it establishes the parsed-event contract that
LocalStream will persist. LocalStream design and contract work may proceed in
parallel, but direct DurableBuffer replacement must not make LocalStream
authoritative for legacy profile-specific `SourceEvent` records.

## Ordered migration

| Phase | Status | Objective | Completion evidence |
| --- | --- | --- | --- |
| 0 | Complete | Align ADRs and authoritative documentation. | Architecture, roadmap, README, and ADRs state one Pipeline, LocalStream boundaries, PDAG, RELP ownership, and no production multiplexer. |
| 1 | Complete | Add Normalize/LocalStream and architecture guards. | Pipeline references Normalize, LocalStream, and RELP (`DeltaZulu.Pipeline.csproj`); `PipelineAssembly_ReferencesOnlyExternalPipelineDependencies`/`PipelineAssembly_ReferencesNormalizeAndLocalStream` reject Agent-layer references and `PipelineAssembly_TransitionalDirectDurableBufferReferenceIsTracked` reports the direct DurableBuffer use in `tests/DeltaZulu.Agent.Tests/ApplicationTests.cs` (and the equivalent in `DomainTests.cs`). Both new assemblies are scaffolds: they exist as real, referenced, tested project boundaries but implement no PDAG or stream runtime yet, which remain later phases. |
| 2 | Active | Define strict input contracts and compile validated acquisition plans. | `TextInputRecord` and `StructuredInputRecord` preserve acquisition metadata; resource configuration separates kind, framing, payload format, admission, parser domain, and deterministic acquisition key. The initial `ExecutionPlanCompiler` normalizes and rejects conflicting physical-resource definitions without replacing runtime execution. |
| 3 | Planned | Generalize acquisition, framing, and decoding through protocol-specific adapters. | `file`, `fifo`, `syslog-tcp`, `syslog-udp`, and `syslog-relp` adapters emit typed records with bounded framing and no application parser dependency. FIFO creation/reopen is explicit configuration, not a syslog behavior. |
| 4 | Planned | Add syslog admission presets. | TCP and UDP may use the same numeric port; TCP supports bounded RFC 6587 octet-counted and newline framing; PRI validation, decoding, size checks, and rejection metrics run before Normalize. Valid unknown syslog reaches Normalize. |
| 5 | Planned | Move structured sources and RELP payload adapters to structured contracts. | CSV, Windows, and MessagePack `DeliveryBatch` sources bypass Normalize; RELP protocol handling remains framing/session work and payload type selects text versus structured materialization. |
| 6 | Planned | Add restricted `parse.query` and a Normalize compatibility materializer. | Existing `filter.query` profiles continue receiving compatible `SourceEvent` shapes; profile-scoped diagnostics validate topic-tagged Normalize rules; raw text and parser provenance are retained. |
| 7 | Planned | Build unified Normalize PDAG generations. | Parser domains group compatible rules deterministically; each plaintext record traverses one PDAG; recognized, unrecognized, and error outcomes remain explicit. |
| 8 | Planned | Move auditd correlation after Normalize. | The assembler consumes normalized fields, maintains bounded incomplete-group state, and retires the hardcoded audit parser in the acquisition adapter. |
| 9 | Planned | Define parsed envelopes and implement the LocalStream host. | `ParsedEventEnvelope` has stable identity and materialization state; one LocalStream host provides append/read/commit/replay for `agent.parsed` and `agent.output`, but legacy DurableBuffer remains the active forwarder until dispatch and ACK semantics are proven. |
| 10 | Planned | Replace profile-centric execution with execution plans. | The planner opens each physical resource once per acquisition key and binds acquisition to parser/materialization/stream plans deterministically; `ProfileBinding` no longer defines a pipeline instance. |
| 11 | Planned | Add coordinated filter dispatch. | Every candidate runs in deterministic order; output append precedes parsed commit; no-candidate, no-match, filter error, and output error remain distinct. |
| 12 | Planned | Migrate forwarding to LocalStream `agent.output` and retire direct DurableBuffer ownership. | The RELP forwarder is an ACK-gated LocalStream subscription with replay; no forwarder-created DurableBuffer host remains Agent-visible. |
| 13 | Planned | Remove daemon multiplexer. | The daemon does not instantiate `ChannelOutputMultiplexer`; lifecycle uses plan-owned tasks, stream drain policy, and only private publisher serialization where required. |
| 14 | Planned | Remove compatibility plaintext parsers. | Normalize-only plaintext parsing has syslog, journal/FIFO, and auditd parity corpora; no transport adapter invokes an application-specific parser. |
| 15 | Planned | Add blindness and end-to-end observability. | Admission, parser, filter, complete-blindness, streams, forwarding, and bounded unknown diagnostics are observable. |
| 16 | Planned | Harden reload and operations. | Parser and filter generations replace atomically; checkpoints, poison policy, retention/storage pressure, graceful drain, and failure injection are covered. |
| 17 | Planned | Archive conflicting operational guidance. | All active examples and commands validate; historical documents are marked superseded. |
| 18 | Planned | Optimize only with benchmarks. | Before/after evidence preserves parsing, commit, queue, and blindness invariants. |

Phases 2–5 establish the strict source boundary. Phases 6–8 are the first
production migration priority because they remove parser ownership overlap and
stabilize materialization semantics. LocalStream contract/design work may run
alongside them, but Phase 12 cannot begin until parsed envelopes, execution
plans, coordinated dispatch, and commit-order tests exist. Phases 9–13 are one
coordinated daemon migration and must not be represented as final architecture
until completed.

## Acceptance gates

### Parsing and profiles

- Compatible plaintext rules compile into one deterministic PDAG per domain.
- Every rule has exactly one `topic.*` tag; full `event.tags` remains queryable.
- Structured sources bypass Normalize; valid unknown plaintext is preserved.
- `parse.query` is optional and separate from Rx.Kql `filter.query`; no profile
  exposes streams, subscriptions, offsets, partitions, generations, or batching.
- A source specification names a transport/acquisition kind separately from
  framing, payload format, admission policy, and Normalize parser domain. The
  planner rejects incompatible combinations and conflicting acquisition keys.

### Durability and dispatch

- One LocalStream host owns `agent.parsed` and `agent.output`, initially with one
  partition each and bounded retention.
- Parsed positions commit only after all output appends succeed or a recorded
  successful zero-output disposition; output positions commit only after RELP ACK.
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
restart, and commit only after a RELP acknowledgement. The direct DurableBuffer
project reference is removed only after those behaviors and their failure tests
are in place.

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
