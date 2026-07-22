# Output Multiplexer Retirement: Decision, Validation, and Roadmap Revision

This document validates and extends a proposal to remove the general-purpose output multiplexer
(`ChannelOutputMultiplexer`) from the target `DeltaZulu.Agent` production pipeline. Section 1 is the decision.
Section 2 is a validation of that decision against the current repository. Sections 3 onward are the corrected,
self-contained roadmap revision, followed by an implementation subtask matrix.

## 1. Decision

The target DeltaZulu.Agent production pipeline shall not contain a general-purpose output multiplexer.

`ChannelOutputMultiplexer` is a compatibility mechanism for the current profile-centric runtime. It serializes
concurrent calls from several independently running profile pipelines into one output writer.

It is not required after the runtime changes to:

* open each physical resource once;
* parse each plaintext record once;
* publish materialized records to `agent.parsed`;
* coordinate filter execution centrally;
* publish accepted outputs to `agent.output`;
* forward through an independent LocalStream subscription.

The class should be retired after the profile-centric runtime is removed.

## 2. Validation against the current repository

This section records what was checked before adopting the decision above, so the roadmap that follows does not
overstate what already exists.

### 2.1 The stated root cause is confirmed

`AgentRuntime.RunMultiple` (`src/DeltaZulu.Agent.Runtime/AgentRuntime.cs`) starts one `ResourcePipeline` per
`ProfileBinding`. Each binding's `CompletionTrackingWriter` is constructed with `completeInner: false` and writes
into one shared `ChannelOutputMultiplexer`, which serializes their concurrent `OnNext` calls into the real sink.
`ChannelOutputMultiplexer` (`src/DeltaZulu.Pipeline/Core/ChannelOutputMultiplexer.cs`) is exactly the bounded
channel (`65_536`, `SingleReader=true`, `FullMode=Wait`) with terminal drain semantics that this document
describes. The decision's premise — the multiplexer exists *because* the runtime duplicates the pipeline per
profile — is accurate, not aspirational.

In the single-binding case (`AgentRuntime.RunSingle`), there is no multiplexer at all: the one pipeline writes
directly through a `CompletionTrackingWriter(_sink, completed)` with the default `completeInner: true`. This
confirms the multiplexer is purely a concurrency-serialization shim for the multi-binding case, not a routing or
durability component.

`CompletionTrackingWriter` (`src/DeltaZulu.Pipeline/Core/CompletionTrackingWriter.cs`) is a separate, smaller
concern (signaling pipeline completion via a `ManualResetEventSlim`) and is correctly scoped for retention in
finite CLI/test workflows, independent of the multiplexer's fate.

The daemon (`src/DeltaZulu.Agent.Daemon/Program.cs`) already runs as a .NET Generic Host with a
`ForwarderDaemonService` hosted service, which is compatible with this document's target lifecycle model
(cancellation tokens, hosted-service lifetime, explicit drain) rather than `IObserver.OnCompleted` propagation.

### 2.2 Preconditions: nothing in the target topology exists yet

A repository-wide search confirms the following identifiers do not appear anywhere in `src/`, `tests/`, or `docs/`
prior to this document: `LocalStream`, `agent.parsed`, `agent.output`, `IParsedEventPublisher`,
`ParsedEventEnvelope`, `LocalStreamParsedEventPublisher`, `FilterDispatcher`, `FilterDispatchResult`,
`FilterDisposition`, `StreamPosition`, and `PDAG`. The target topology described in Sections 3–9 below is
**entirely greenfield**. No part of it is partially implemented today. Every phase that depends on this substrate
is gated accordingly in the subtask matrix (Section 12).

Likewise, `docs/adr/` contains only four ADRs (0001 Windows eventing library boundaries, 0002 Windows SID
observation boundary, 0003 parser dispatch and FORWARDER-native boundaries, 0003 profile KQL preserves source event
shape, 0004 KQL capability alignment). There is no existing "LocalStream ADR" or "unified runtime ADR" to add
language to, and no existing document defines phase numbers for this migration. This document is therefore
written to be self-contained: Section 3 below is an explicit "Phase 0" that produces the ADRs this revision
otherwise assumes are already in place, and all later phase numbers anchor to sections in this file.

### 2.3 LocalStream is not a durability replacement for DeltaZulu.DurableBuffer

The repository's architecture discipline (`docs/ROADMAP.md`, "Architecture discipline") states that
`DeltaZulu.DurableBuffer` is the authoritative durability and backpressure layer ahead of FORWARDER dispatch. LocalStream,
as introduced by this document, is the **in-agent fan-in/fan-out substrate** between acquisition, parsing, and
filtering — it is not a claim of replacing or duplicating FORWARDER delivery durability. The forwarder subscription
(Section 8) still enqueues into `DeltaZulu.DurableBuffer` before FORWARDER dispatch; "no second queue before
`agent.parsed`" refers to application-level routing queues, not to the existing durable buffer.

### 2.4 Test and cleanup location correction

The multiplexer's and completion writer's unit tests currently live in
`tests/DeltaZulu.Agent.Tests/ApplicationTests.cs` (`ChannelOutputMultiplexer_*` and `CompletionTrackingWriter_*`
test methods), inside the shared agent test project — there is no separate "daemon test project." Section 12
(Phase 12 / WS-H) cleanup targets that file specifically, removing only the multiplexer cases and retaining the
completion-writer cases.

### 2.5 Verdict

The decision and its reasoning are sound and are adopted without reversal. Validation surfaced only grounding and
anchoring corrections (2.2–2.4), which are folded into the roadmap below and the deletion condition remains
unchanged from the original proposal: **delete `ChannelOutputMultiplexer` when `AgentRuntime` no longer starts one
`ResourcePipeline` per `ProfileBinding`.**

## 3. Why the current multiplexer exists

The current runtime treats a profile as an execution unit:

```text
ProfileBinding
    Input
    Profile
    Executor
```

Several bindings run concurrently:

```text
binding A → ResourcePipeline A ─┐
binding B → ResourcePipeline B ─┼→ shared output writer
binding C → ResourcePipeline C ─┘
```

The shared writer may not support concurrent `OnNext` calls. The multiplexer therefore provides:

* multiple concurrent writers;
* one serialized reader;
* bounded buffering;
* shared completion;
* shared error capture.

This solves a real problem in the current architecture, but the problem is caused by duplicating the pipeline per
profile.

The new architecture must remove the duplication instead of preserving the multiplexer as permanent infrastructure.

## 4. Target topology

```text
Acquisition source A ─┐
Acquisition source B ─┼→ agent.parsed
Acquisition source C ─┘
                            ↓
                   filter dispatcher
                            ↓
                       agent.output
                            ↓
                    FORWARDER forwarder
```

There are three apparent fan-in or fan-out points. None requires a general multiplexer.

## 5. Acquisition fan-in

Several acquisition sources may append records to `agent.parsed`.

```text
TCP syslog ──────┐
UDP syslog ──────┤
file tail ───────┼→ agent.parsed
Event Log ───────┤
ETW ─────────────┘
```

This is not an application-level multiplexer.

The `agent.parsed` LocalStream producer boundary is responsible for accepting concurrent appends and assigning
stream positions.

The Pipeline should use one publisher abstraction:

```csharp
public interface IParsedEventPublisher
{
    ValueTask<StreamPosition> PublishAsync(
        ParsedEventEnvelope envelope,
        CancellationToken cancellationToken = default);
}
```

All acquisition/materialization workers call this publisher.

If the LocalStream producer does not permit concurrent calls, serialization belongs inside
`LocalStreamParsedEventPublisher`:

```text
many source tasks
    → one publisher adapter
    → LocalStream producer
```

That is an implementation lock or channel around one producer. It is not a routing multiplexer and should not be
exposed as a first-class pipeline component.

The publisher must not:

* inspect logical topics to select physical streams;
* copy one event to several physical topics;
* coordinate filters;
* track profile completion;
* implement durable buffering independently of LocalStream.

## 6. Filter fan-out

One parsed event may be evaluated by several filters:

```text
parsed event
    ├── topic-bound filter A
    ├── topic-bound filter B
    └── catch-all filter
```

This is coordinated dispatch, not stream multiplexing.

The filter dispatcher owns one input event at a time and produces a result:

```csharp
public sealed record FilterDispatchResult
{
    public required string EventId { get; init; }

    public required IReadOnlyList<ResourceOutputRecord> Outputs { get; init; }

    public required FilterDisposition Disposition { get; init; }
}
```

The dispatcher:

1. identifies applicable filters;
2. evaluates each filter;
3. gathers emitted rows;
4. appends the output rows to `agent.output`;
5. records the final disposition;
6. commits the `agent.parsed` position.

The dispatcher needs a result aggregator local to the input event. It does not need a global output multiplexer.

### 6.1 Sequential first implementation

The first implementation should execute applicable filters sequentially:

```text
filter A
    → filter B
    → catch-all filter
    → append collected outputs
```

Advantages:

* deterministic filter order;
* simple failure semantics;
* bounded memory per event;
* simple blindness accounting;
* no concurrent Rx.Kql execution concerns;
* no output serialization component.

### 6.2 Optional future parallel execution

If benchmarks later justify parallel filter execution:

```text
event
    → parallel filter tasks
    → Task.WhenAll
    → deterministic result ordering
    → one output append operation
```

The results should be ordered by:

```text
filter ID
filter version
output ordinal
```

Parallel filter evaluation still does not require `ChannelOutputMultiplexer`.

## 7. Output fan-in

The coordinated filter dispatcher is the logical writer to `agent.output`.

```text
filter dispatch result
    → zero or more deterministic output records
    → agent.output
```

Even when several `agent.parsed` partitions are introduced later, each partition processor should append through
the LocalStream producer adapter.

The LocalStream storage boundary handles concurrent producer appends. A separate in-memory multiplexer would add:

* another queue;
* another backpressure boundary;
* another shutdown protocol;
* another error path;
* another place where records can exist without durable acknowledgement.

That would weaken rather than improve the durability model (see Section 2.3 on the LocalStream /
`DeltaZulu.DurableBuffer` boundary).

## 8. Output fan-out

Several consumers may need the accepted output stream:

```text
agent.output
    ├── FORWARDER forwarder subscription
    ├── local NDJSON diagnostic subscription
    └── future local inspection subscription
```

This is LocalStream subscription fan-out.

Each consumer has:

* its own subscription identity;
* its own checkpoint;
* its own lag;
* its own retry behavior.

No output multiplexer is needed.

The runtime should not read one record and manually invoke several sinks. Doing so would couple their failure and
acknowledgement semantics.

## 9. Routing by `event.tags`

The extraction of `topic.*` from `event.tags` is not multiplexing.

```text
event.tags = [
    topic.sshd,
    authentication.failure
]
```

becomes:

```text
envelope.Topic = sshd
```

The topic is used by the filter registry to select candidate filters.

```text
Topic = sshd
    → sshd filters
    + Syslog catch-all filters
```

The record remains in one physical `agent.parsed` stream.

There is no need to copy it into several application-specific streams.

## 10. Completion and lifecycle

The current multiplexer also helps coordinate completion from several independently running profile pipelines.

That lifecycle model disappears from the daemon.

Long-running services should be supervised explicitly:

```text
PipelineRuntime
    Acquisition workers
    Materialization workers
    Parsed-stream processor
    Output-stream forwarder
```

Shutdown order should be:

1. Stop accepting new acquisition work.
2. Finish or cancel in-flight materialization.
3. Flush accepted events to agent.parsed.
4. Stop parsed-stream processing after the configured drain policy.
5. Flush outputs to agent.output.
6. Stop the forwarder after the configured drain policy.
7. Dispose LocalStream.

Service lifecycle should be expressed through:

* cancellation tokens;
* hosted-service lifetime;
* task completion;
* explicit drain methods.

It should not depend on propagating `IObserver.OnCompleted` through a multiplexer.

`CompletionTrackingWriter` may remain useful for finite CLI and test workflows, but it is not part of the daemon's
target architecture.

## 11. Error isolation

A multiplexer currently collects errors from concurrent profile pipelines.

The target runtime gives each stage an explicit failure boundary.

### Acquisition failure

```text
source worker fails
    → source health metric
    → mandatory/optional resource policy
    → restart, disable, or fail runtime
```

### Parser failure

```text
materialization fails operationally
    → parser error metric
    → retry or poison-record policy
```

### Filter failure

```text
filter fails
    → do not classify as no-match
    → retry or isolate filter according to policy
```

### Parsed stream failure

```text
append or commit fails
    → do not advance processing
```

### Forwarding failure

```text
FORWARDER send fails
    → do not commit agent.output position
```

There is no need for one shared multiplexer error property.

## 12. Transitional treatment of `ChannelOutputMultiplexer`

Do not remove the current class at the beginning of the refactoring. The existing multi-binding runtime still
requires serialized access to a shared `IOutputWriter`.

Treat it as transitional:

```text
Current runtime:
    required

LocalStream output migration:
    still required for legacy profile pipelines writing to one LocalStream writer

Coordinated dispatcher activated:
    no longer required in production

ProfileBinding runtime removed:
    delete or restrict to legacy CLI path
```

If the CLI or workbench still runs several independent queries against one console/file writer, retain a smaller
adapter named according to its actual function:

```text
SerializedOutputWriter
```

This adapter would:

* serialize concurrent calls;
* contain no routing;
* contain no durable buffering;
* contain no profile semantics;
* remain outside the daemon production path.

Calling it a multiplexer would be misleading because it neither routes nor duplicates records.

## 13. Revised component structure

Remove this from the target production structure:

```text
Core/
    ChannelOutputMultiplexer.cs
```

Use:

```text
Streaming/
    ParsedEventPublisher.cs
    OutputEventPublisher.cs
    FilterDispatcher.cs
    FilterDispatchResult.cs
```

Optional compatibility location:

```text
Legacy/
    SerializedOutputWriter.cs
```

or:

```text
Agent.ProfileWorkbench/
    SerializedOutputWriter.cs
```

depending on the remaining consumer.

## 14. Roadmap changes

### Phase 0 — Architecture decisions and preconditions

This phase did not exist in the original proposal; it is added here to close the anchoring gap identified in
Section 2.2, so every later phase resolves against documents that actually exist in this repository.

Add a new **LocalStream ADR**:

> LocalStream topics and subscriptions provide durable in-agent fan-in and fan-out. The Pipeline shall not
> introduce a separate general-purpose multiplexer between acquisition, filtering, output persistence, and
> forwarding. `DeltaZulu.DurableBuffer` remains the authoritative durability and backpressure layer for FORWARDER
> dispatch; LocalStream does not replace it.

Add a new **unified runtime ADR**:

> Filter dispatch is coordinated per parsed event. The dispatcher aggregates filter results before output
> publication and does not use independent profile pipelines writing through a shared multiplexer.

Exit criteria for Phase 0:

* both ADRs exist under `docs/adr/`;
* every "Phase N" reference below resolves to a section in this document;
* `docs/ROADMAP.md` links to this document.

### Phase 1 — Input contracts

No change.

Inputs emit text or structured records.

They do not write directly to a shared output sink.

### Phase 2 — Acquisition and framing

Add one shared `IParsedEventPublisher` abstraction.

Do not add an acquisition multiplexer.

Acceptance criteria:

* several acquisition workers can publish concurrently;
* every accepted event receives one durable stream position;
* publisher concurrency behavior is tested;
* no second queue exists before LocalStream.

### Phase 3 — Syslog TCP and UDP

TCP and UDP listeners independently submit admitted records to the shared materialization path.

Do not merge them through `ChannelOutputMultiplexer`.

Acceptance criteria:

* TCP and UDP may run concurrently;
* transport metadata identifies the source protocol;
* both feed the same parser domain;
* LocalStream append ordering is documented;
* no artificial global ordering across TCP and UDP is promised unless one partition and serialized publication
  provide it.

### Phase 4 — Structured inputs

Structured inputs publish through the same parsed-event publisher.

No multiplexer is introduced between structured and text-derived events.

### Phase 5 — LocalStream output migration

Implement:

```text
Legacy ResourcePipeline outputs
    → LocalStreamOutputWriter
    → agent.output
```

During this transitional phase, the existing multiplexer may remain because several legacy profile pipelines can
still call one LocalStream-backed writer concurrently.

Do not treat it as the final output architecture.

Add a deletion condition:

```text
Delete ChannelOutputMultiplexer when AgentRuntime no longer starts
one ResourcePipeline per ProfileBinding.
```

### Phase 6 — Parse-query contract

No multiplexer-related change.

### Phase 7 — Unified PDAG

All compatible rules compile into one parser.

No parser multiplexer or per-profile parser fan-out is introduced.

### Phase 8 — `agent.parsed`

Multiple materializers append through one publisher adapter.

Add concurrency tests:

* concurrent text and structured publication;
* cancellation during append;
* producer failure;
* stable event identity;
* no record duplication caused by publisher serialization.

### Phase 9 — Execution-plan runtime

This phase eliminates the architectural reason for `ChannelOutputMultiplexer`.

Replace:

```text
many ProfileBinding pipelines
    → ChannelOutputMultiplexer
    → shared sink
```

with:

```text
acquisition plans
    → parser/materialization plans
    → agent.parsed
    → one filter-dispatch processor
```

Exit criteria:

* the daemon does not instantiate `ChannelOutputMultiplexer`;
* the daemon does not start one `ResourcePipeline` per profile;
* each physical resource is opened once;
* each plaintext record is parsed once;
* output enters `agent.output` through the dispatcher.

### Phase 10 — Coordinated filtering

Implement per-event output aggregation.

Initial behavior:

```text
sequential applicable-filter evaluation
    → local output list
    → deterministic IDs
    → append to agent.output
```

Do not use a shared multiplexer.

Exit criteria:

* all candidate filters are conclusively evaluated;
* zero-output disposition is known;
* output order is deterministic;
* one append failure prevents parsed-position commit;
* no global in-memory fan-in queue exists.

### Phase 11 — Auditd assembly

No multiplexer-related change.

The assembler emits completed events through the parsed-event publisher.

### Phase 12 — Remove legacy parsers and runtime infrastructure

Remove:

* hardcoded plaintext parsers;
* per-profile daemon pipelines;
* `ChannelOutputMultiplexer` from the production runtime;
* multiplexer-specific tests in `tests/DeltaZulu.Agent.Tests/ApplicationTests.cs`.

Retain or rename a serialized writer only if CLI/workbench concurrency still requires it, and keep the
`CompletionTrackingWriter` test cases in the same file — only the multiplexer cases are retired.

### Phase 13 — Observability

Do not expose multiplexer metrics in the target model.

Replace them with stage metrics:

```text
parsed publisher append latency
parsed publisher failures
filter dispatch duration
filter output count
agent.output append latency
stream lag
stream offset expiry
forwarder latency
```

### Phase 14 — Hot reload

Parser and filter generation swaps update immutable registries.

They do not create parallel old/new output pipelines writing through a multiplexer.

Blue-green activation should occur at a generation boundary:

```text
build generation N+1
    → validate
    → atomic active-generation swap
    → new events use N+1
    → in-flight N events finish
```

Both generations may briefly execute concurrently, but their outputs append through the normal LocalStream producer
adapter. No special multiplexer is required.

## 15. Updated acceptance criteria

1. The daemon does not instantiate `ChannelOutputMultiplexer`.
2. No general-purpose application queue exists before `agent.parsed`.
3. No general-purpose application queue exists between filter dispatch and `agent.output`.
4. Multiple acquisition sources publish through the LocalStream producer boundary.
5. Filter result aggregation is scoped to one parsed event.
6. LocalStream subscriptions provide output fan-out.
7. Parallel filter execution, if introduced, uses deterministic per-event aggregation.
8. Completion is managed by service tasks and cancellation, not observer multiplexing.
9. `ChannelOutputMultiplexer` is deleted or restricted to a legacy CLI/workbench adapter.
10. The production architecture contains no component called a multiplexer.

## 16. Final invariant

LocalStream provides durable fan-in and fan-out.

The parser provides one combined structural decision.

The filter dispatcher provides one coordinated coverage decision.

The forwarder provides one acknowledged delivery path.

No separate production multiplexer is required.

## 17. Implementation subtask matrix

This matrix extends Section 14's phases into concrete, dependency-ordered subtasks. Columns: **ID · Subtask ·
Depends on · Target boundary (files) · Acceptance signal · Risk**. Workstreams map onto the phases above; WS-A is
the only workstream that safely touches today's code before the LocalStream substrate (Section 2.2) exists.

### WS-0 — Preconditions & decisions (Phase 0)

| ID | Subtask | Depends on | Target boundary | Acceptance | Risk |
|----|---------|-----------|-----------------|-----------|------|
| P0.1 | Author the LocalStream ADR (topics, subscriptions, positions, fan-in/fan-out, relationship to `DeltaZulu.DurableBuffer`). | — | `docs/adr/0005-localstream-fan-in-fan-out.md` | ADR states LocalStream is the in-agent substrate; DurableBuffer stays the FORWARDER durability layer; no general multiplexer. | Low |
| P0.2 | Author the unified-runtime ADR (per-event coordinated filter dispatch; no per-profile pipelines through a shared multiplexer). | — | `docs/adr/0006-unified-execution-plan-runtime.md` | ADR captures the two invariants quoted in Section 14, Phase 0. | Low |
| P0.3 | Link this document from `docs/ROADMAP.md`. | P0.1, P0.2 | `docs/ROADMAP.md` | Roadmap references this file near pipeline extraction / architecture discipline. | Low |

### WS-A — Transitional hardening (do-now, touches current code) (Section 12, Phase 5)

| ID | Subtask | Depends on | Target boundary | Acceptance | Risk |
|----|---------|-----------|-----------------|-----------|------|
| A.1 | Add an explicit deletion-condition comment on `ChannelOutputMultiplexer` and `AgentRuntime.RunMultiple`. | — | `ChannelOutputMultiplexer.cs`, `AgentRuntime.cs:109` | Both sites reference "delete when runtime stops one pipeline per binding." | Low |
| A.2 | Introduce `SerializedOutputWriter` (serialize-only; no routing/buffering/profile semantics) for the CLI/workbench concurrency case; keep `ChannelOutputMultiplexer` unchanged for the daemon. | A.1 | new `src/DeltaZulu.Pipeline/Core/SerializedOutputWriter.cs` (or `Legacy/`) | Serializes concurrent `OnNext`; unit-tested; not on the daemon production path. | Med |
| A.3 | Confirm `CompletionTrackingWriter` stays for finite CLI/test flows; document it is not the daemon target lifecycle. | — | `CompletionTrackingWriter.cs` xml-doc | Doc note added; existing tests unchanged. | Low |

### WS-B — LocalStream substrate (greenfield) (Phases 2, 8)

| ID | Subtask | Depends on | Target boundary | Acceptance | Risk |
|----|---------|-----------|-----------------|-----------|------|
| B.1 | Implement LocalStream producer/subscription core (positions, concurrent appends, subscription checkpoints/lag). | P0.1 | new `src/DeltaZulu.Pipeline/Streaming/` | Concurrent producers get monotonic positions; independent subscriber checkpoints. | High |
| B.2 | Define `agent.parsed` and `agent.output` topics. | B.1 | `.../Streaming/` | Two named streams instantiated by the runtime. | Med |
| B.3 | Clarify LocalStream ↔ `DeltaZulu.DurableBuffer` boundary in code paths (FORWARDER durability stays in the buffer). | B.1 | `src/DeltaZulu.Pipeline/Outputs/Relp/BufferedRelpSink.cs` | Forwarder still enqueues to DurableBuffer; no records unacknowledged outside it. | High |

### WS-C — Acquisition fan-in / parsed publisher (Phases 2–4, 11)

| ID | Subtask | Depends on | Target boundary | Acceptance | Risk |
|----|---------|-----------|-----------------|-----------|------|
| C.1 | Add `IParsedEventPublisher` + `LocalStreamParsedEventPublisher` (serialization inside the adapter, not exposed as a component). | B.2 | `.../Streaming/ParsedEventPublisher.cs` | Many acquisition workers publish concurrently; one durable position each; concurrency-tested. | Med |
| C.2 | Route all inputs (syslog TCP/UDP, files, auditd, Windows Event Log/EVTX/ETL/ETW, FORWARDER) through the publisher instead of a shared sink. | C.1 | `src/DeltaZulu.Pipeline/Inputs/**` | No input writes directly to an output writer; no second queue before `agent.parsed`. | High |
| C.3 | Auditd assembler emits completed events via the publisher. | C.1 | `src/DeltaZulu.Pipeline/Inputs/Auditd/**` | EOE/PROCTITLE-completed events publish once. | Med |

### WS-D — Unified parser / parse-query (Phase 7)

| ID | Subtask | Depends on | Target boundary | Acceptance | Risk |
|----|---------|-----------|-----------------|-----------|------|
| D.1 | Extract `topic.*` from `event.tags` into `envelope.Topic` (routing hint only; record stays in one physical stream). | C.1 | `.../Streaming/` + parser | `topic.sshd` → `Topic=sshd`; no event duplication across streams. | Med |
| D.2 | Compile compatible rules into one parser (PDAG); parse each plaintext record once. | D.1 | parser project | Each physical resource opened once, each record parsed once. | High |

### WS-E — Coordinated filter dispatch (Phase 10)

| ID | Subtask | Depends on | Target boundary | Acceptance | Risk |
|----|---------|-----------|-----------------|-----------|------|
| E.1 | Implement `FilterDispatchResult` / `FilterDisposition` and a per-event dispatcher (topic-bound + catch-all), sequential first. | D.1 | `.../Streaming/FilterDispatcher.cs` | All candidate filters evaluated; deterministic output order; zero-output disposition explicit. | High |
| E.2 | Dispatcher appends outputs to `agent.output` and commits the `agent.parsed` position only on successful append. | E.1, B.2 | `.../Streaming/` | One append failure blocks the parsed-position commit; no global fan-in queue. | High |
| E.3 | (Optional, benchmark-gated) parallel filter execution with deterministic per-event aggregation (filter id → version → ordinal). | E.1 | `.../Streaming/` | Parallel path yields identical ordering; still no multiplexer. | Med |

### WS-F — Output fan-out & forwarder via subscriptions (Phases 6–7)

| ID | Subtask | Depends on | Target boundary | Acceptance | Risk |
|----|---------|-----------|-----------------|-----------|------|
| F.1 | FORWARDER forwarder consumes `agent.output` as an independent LocalStream subscription (own checkpoint/lag/retry). | E.2, B.3 | `src/DeltaZulu.Pipeline/Outputs/Relp/**` | Forwarder is a subscriber; FORWARDER-send failure does not commit its `agent.output` position. | High |
| F.2 | Diagnostic NDJSON consumer as a separate subscription (no manual multi-sink invocation). | E.2 | `src/DeltaZulu.Pipeline/Outputs/Ndjson/**` | NDJSON and FORWARDER failures are independent. | Med |

### WS-G — Execution-plan runtime → remove per-profile pipelines (Phase 9; triggers deletion)

| ID | Subtask | Depends on | Target boundary | Acceptance | Risk |
|----|---------|-----------|-----------------|-----------|------|
| G.1 | Replace `AgentRuntime` per-`ProfileBinding` pipelines with acquisition/parse plans feeding one filter-dispatch processor. | C.2, D.2, E.2, F.1 | `AgentRuntime.cs`, `ResourcePipeline.cs`, `Program.cs:CreateBindings` | Daemon starts no `ResourcePipeline` per binding; deletion condition (Section 12) met. | High |
| G.2 | Cancellation/hosted-service shutdown drain (Section 10 order); stop relying on `IObserver.OnCompleted` propagation. | G.1 | `ForwarderDaemonService`, `AgentRuntime.cs` | Ordered drain: stop acquisition → flush parsed → drain dispatch → flush output → stop forwarder → dispose LocalStream. | High |

### WS-H — Cleanup, observability, hot reload (Phases 12–14)

| ID | Subtask | Depends on | Target boundary | Acceptance | Risk |
|----|---------|-----------|-----------------|-----------|------|
| H.1 | Delete `ChannelOutputMultiplexer` from the production runtime; retain/rename only `SerializedOutputWriter` if CLI/workbench still needs it. | G.1 | `ChannelOutputMultiplexer.cs` | Type gone from daemon path; no production construction remains. | Med |
| H.2 | Migrate tests: remove `ChannelOutputMultiplexer_*` cases, keep `CompletionTrackingWriter_*`, add LocalStream/publisher/dispatcher concurrency tests. | H.1 | `tests/DeltaZulu.Agent.Tests/ApplicationTests.cs` (+ new) | Suite green; new concurrency/ordering tests present. | Med |
| H.3 | Replace multiplexer metrics with stage metrics (publisher append latency/failures, dispatch duration, output append latency, stream lag/offset expiry, forwarder latency). | G.2 | `src/DeltaZulu.Pipeline/Core/Observability/**` | No multiplexer metric surface; stage metrics emitted. | Med |
| H.4 | Hot reload = immutable parser/filter registry generation swap (blue-green at a generation boundary; both generations append through the normal producer adapter, no parallel multiplexed pipelines). | D.2, E.1 | parser/filter registries | Generation swap validated; no multiplexer reintroduced. | Med |
| H.5 | Update `README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md` to the post-multiplexer architecture. | H.1 | docs | Docs show LocalStream/dispatcher runtime; no stale multiplexer references. | Low |

The final invariant in Section 16 is the exit test for this matrix: once WS-H completes, the production
architecture contains no component called a multiplexer, LocalStream provides durable fan-in/fan-out, the parser
makes one structural decision, the dispatcher makes one coverage decision, and the forwarder provides one
acknowledged delivery path.
