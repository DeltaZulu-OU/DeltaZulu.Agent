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
Phase 1 (marker types only; the PDAG compiler and stream runtime are Phase 7-8
and Phase 6/10 work). The daemon still runs separate
`ProfileBinding`/`ResourcePipeline` work and uses `ChannelOutputMultiplexer` to
serialize concurrent legacy output. Those are transitional implementation
details, not target architecture. Existing syslog and auditd parsing is also
transitional until Normalize parity is established.

## Current delivery status (2026-07-17)

**We have completed the architectural foundation (Phases 0 and 1) and are now
working on Phase 2: input contracts.** The immediately planned implementation
is to introduce `TextInputRecord` and `StructuredInputRecord`, then adapt
existing inputs without losing their current source metadata. This establishes
the stable text boundary required before Phase 7 can add `parse.query` and
Phase 8 can compile a unified PDAG.

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
the daemon runtime. Phases 3-5 and 6 remain separate follow-on tracks as
described below.

## Ordered migration

| Phase | Status | Objective | Completion evidence |
| --- | --- | --- | --- |
| 0 | Complete | Align ADRs and authoritative documentation. | Architecture, roadmap, README, and ADRs state one Pipeline, LocalStream boundaries, PDAG, RELP ownership, and no production multiplexer. |
| 1 | Complete | Add Normalize/LocalStream and architecture guards. | Pipeline references Normalize, LocalStream, and RELP (`DeltaZulu.Pipeline.csproj`); `PipelineAssembly_ReferencesOnlyExternalPipelineDependencies`/`PipelineAssembly_ReferencesNormalizeAndLocalStream` reject Agent-layer references and `PipelineAssembly_TransitionalDirectDurableBufferReferenceIsTracked` reports the direct DurableBuffer use in `tests/DeltaZulu.Agent.Tests/ApplicationTests.cs` (and the equivalent in `DomainTests.cs`). Both new assemblies are scaffolds: they exist as real, referenced, tested project boundaries but implement no PDAG or stream runtime yet, which remain later phases. |
| 2 | Active | Introduce text/structured input contracts and adapters. | New inputs emit `TextInputRecord` or `StructuredInputRecord`; compatibility paths preserve metadata. |
| 3 | Planned | Generalize acquisition, framing, and decoding. | TCP, UDP, file, and FIFO emit text records with bounded rejection metrics and no application parser dependency. |
| 4 | Planned | Add the TCP/UDP syslog admission preset. | Same-port TCP/UDP, RFC 6587/newline framing, PRI admission, and unknown valid syslog reaches Normalize. |
| 5 | Planned | Move structured sources and RELP adapters to structured contracts. | CSV/Windows/RELP structured payloads bypass Normalize; Pipeline RELP code is payload mapping only. |
| 6 | Planned | Migrate forwarding to LocalStream `agent.output`. | ACK-gated subscription commits and replay; no forwarder-created DurableBuffer host. |
| 7 | Planned | Add optional restricted `parse.query`. | Existing profiles remain valid; profile-scoped diagnostics validate topic-tagged Normalize rules. |
| 8 | Planned | Build unified PDAG generations. | Deterministic domain grouping/hash/order; one Normalize operation per plaintext record; unmatched records retained. |
| 9 | Planned | Move auditd correlation after Normalize. | Assembler consumes normalized fields; bounded incomplete-group state; hardcoded audit parser retired. |
| 10 | Planned | Create `agent.parsed` and parsed envelopes. | Structured, recognized, unrecognized, and assembled records publish with deterministic IDs and replay. |
| 11 | Planned | Replace profile-centric execution with execution plans. | Each physical resource opens once and binds to parser/filter/stream plans deterministically. |
| 12 | Planned | Add coordinated filter dispatch. | Every candidate runs; output append precedes parsed commit; no-candidate/no-match/errors differ. |
| 13 | Planned | Remove daemon multiplexer. | Daemon does not instantiate `ChannelOutputMultiplexer`; lifecycle uses tasks and drain policy. |
| 14 | Planned | Remove hardcoded plaintext parsers. | Normalize-only plaintext parsing with syslog/auditd parity corpora. |
| 15 | Planned | Add blindness and end-to-end observability. | Admission, parser, filter, complete-blindness, streams, forwarding, and bounded unknown diagnostics are observable. |
| 16 | Planned | Harden reload and operations. | Atomic generations, stable checkpoints, poison policy, retention/storage pressure, graceful drain, failure injection. |
| 17 | Planned | Archive conflicting operational guidance. | All active examples and commands validate; historical documents are marked superseded. |
| 18 | Planned | Optimize only with benchmarks. | Before/after evidence preserves parsing, commit, queue, and blindness invariants. |

Phases 6 and 2–5 may proceed independently. Phases 7–8 require stable text
contracts. Phases 10–13 are one coordinated daemon migration and must not be
represented as final architecture until completed.

## Acceptance gates

### Parsing and profiles

- Compatible plaintext rules compile into one deterministic PDAG per domain.
- Every rule has exactly one `topic.*` tag; full `event.tags` remains queryable.
- Structured sources bypass Normalize; valid unknown plaintext is preserved.
- `parse.query` is optional and separate from Rx.Kql `filter.query`; no profile
  exposes streams, subscriptions, offsets, partitions, generations, or batching.

### Durability and dispatch

- One LocalStream host owns `agent.parsed` and `agent.output`, initially with one
  partition each and bounded retention.
- Parsed positions commit only after all output appends succeed or a recorded
  successful zero-output disposition; output positions commit only after RELP ACK.
- Logical topics remain envelope properties. No `parsed.sshd`-style streams and
  no general-purpose daemon multiplexer are introduced.

### Runtime and coverage

- A physical resource opens once per deterministic acquisition key.
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

## Phase 11 and 13 implementation notes

Concrete anchors for the two phases that retire the current profile-per-source
runtime, so migration work has file-level targets in addition to the
architecture-level objective above.

**Phase 11 — execution plans replace `ProfileBinding`/`ResourcePipeline`:** the
current legacy code is `src/DeltaZulu.Agent.Runtime/AgentRuntime.cs`
(`RunSingle`/`RunMultiple`/`RunBinding`), `ProfileBinding.cs`, and
`src/DeltaZulu.Pipeline/Core/ResourcePipeline.cs`. `AgentRuntime.RunMultiple`
(`AgentRuntime.cs:109`) is the exact place that currently starts one
`ResourcePipeline` per `ProfileBinding`; this phase replaces that with
acquisition/parser/filter plan binding.

**Phase 13 — remove `ChannelOutputMultiplexer`:** the deletion condition is
`AgentRuntime` no longer starting one `ResourcePipeline` per `ProfileBinding`
(i.e., Phase 11 complete). Until then, `ChannelOutputMultiplexer`
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
