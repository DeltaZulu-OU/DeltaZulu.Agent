# ADR 0014: MessagePack is the type-contract wire format; Avro is superseded

## Status

Accepted. Supersedes the wire-serialization choice in
[ADR 0010](0010-type-catalog-avro-arrow-and-ndjson-edge-dialect.md).

## Context

ADR 0010 decided Avro as the agent-to-collector wire serialization and listed
MessagePack among rejected alternatives ("self-describing tags do not provide
a catalog authority, sink DDL, or query-translation contract"). That
rejection was recorded as if it were the final decision; it was an
alternative under consideration, not a decision this project is bound to.
DeltaZulu does not want an Avro wire format at all, on this transport or any
other leg of the pipeline.

Independently, the upstream `DeltaZulu.Forward` library — the transport ADR
0011 names — has already implemented its typed-batch payload as
**MessagePack**, not Avro: `ForwardLogBatchCodec` encodes/decodes
`ForwardLogBatch` via `MessagePackSerializer`, and `ForwardValueNormalizer`
restricts every field to the same KQL-scalar/dynamic-map/dynamic-array set
ADR 0010 wanted an Avro schema to enforce (`bool`, `long`, `double`,
`string`, `DateTimeOffset`, `TimeSpan`, `Guid`, `decimal`, maps, arrays,
`null`) — throwing `NotSupportedException` on anything outside that set
rather than passing it through silently. `ForwardFrameType.TypedBatch`'s
contract is explicitly documented upstream as "one MessagePack-encoded
`ForwardLogBatch`." This ADR ratifies that already-built reality as the
project's own decision, rather than continuing to describe an Avro wire
format nothing implements.

`ForwardLogRecord` also carries typed record identity — `AgentId`,
`SourceType`, `SourceName`, `ProfileId`, `ProfileVersion`, `Platform`,
`Hostname` — split out of a single `SourceId` string in a recent upstream
change. That split matters here: without an embedded Avro schema, the
collector needs another way to know which catalog entry governs a given
record's fields. Per-record source/profile identity is that other way — the
collector resolves the catalog entry from `(SourceType, SourceName,
ProfileId, ProfileVersion)` rather than from a schema carried on the wire.

## Decision

The agent-to-collector wire format is **MessagePack**, carried as a
`ForwardLogBatch` inside a `DeltaZulu.Forward` `TypedBatch` frame (ADR 0011),
matching `DeltaZulu.Forward`'s own implementation rather than a
DeltaZulu.Pipeline-side reimplementation of the encoding. The type-contract
catalog (ADR 0010) keeps its role as the single authority for KQL scalar,
logical annotation, nullability, and unit metadata per source/field — only
the wire-serialization choice changes. Field-level type enforcement moves
from an Avro schema to normalization-at-encode-time (`ForwardValueNormalizer`
upstream) plus catalog-driven validation at the collector, keyed by the
record's own source/profile identity rather than a wire-carried schema.

The catalog's generated-projection list changes from five to four: Arrow
in-memory schema, Proton DDL, DuckDB DDL, and parser contracts. There is no
generated Avro wire schema projection; MessagePack's self-describing map
encoding does not need one, and the catalog validates decoded records
directly against its KQL-scalar/logical-annotation model instead of against
a schema artifact. (The Arrow projection is itself superseded by
[ADR 0015](0015-no-arrow-catalog-typed-records.md), decided after this ADR;
the projection list narrows further to three there.)

The collector decodes each MessagePack `ForwardLogBatch` record once and
validates it against the catalog. **Update
([ADR 0015](0015-no-arrow-catalog-typed-records.md)):** that decode step
does not produce an Arrow record batch — the collector
works directly with the catalog-typed record. This ADR's original text
here claimed Arrow remained the collector's internal representation,
unchanged from ADR 0010; that claim is superseded by ADR 0015.

## Consequences

- ADR 0010's type-contract-catalog authority (KQL scalars, logical
  annotations, nullability, units, per-backend physical mappings) is
  preserved in full; only its Avro-wire clause is superseded. ADR 0010's
  status is updated to reflect this narrow supersession.
- ADR 0011's "one Avro batch per frame" language is corrected to "one
  MessagePack-encoded `ForwardLogBatch` per frame" — this is a documentation
  correction to match what `DeltaZulu.Forward` already does, not a transport
  redesign; framing, handshake, windowing, and dedup are unaffected.
- **Reopened, then mooted:** ADR 0012 justified the Kafka-API-compatible
  Proton intermediate partly on "Avro is a first-class payload format on
  Kafka-protocol infrastructure, so the wire format and the Proton
  intermediate speak the same serialization without re-encoding." That
  no longer held once the wire format became MessagePack, which this ADR
  originally left as an open question for ADR 0012's Phase 3b testing.
  [ADR 0016](0016-bespoke-proton-sink-supersedes-kafka-intermediate.md)
  has since dropped the Kafka-API intermediate entirely in favor of a
  bespoke Proton sink, which moots the question rather than answering it.
- No source code in this repository referenced Avro at the time of this
  ADR (verified by search); this is a pre-implementation documentation
  correction, not a migration.

## Alternatives rejected

- **Avro** (ADR 0010's original decision): rejected per this ADR's own
  premise — DeltaZulu does not want an Avro wire format. Avro's
  writer/reader schema-resolution machinery is not needed once
  per-record source/profile identity lets the collector resolve catalog
  metadata without a wire-carried schema.
- **Arrow IPC directly on the wire**: not adopted; ADR 0010's objection
  (Arrow schema evolution is batch-local, operationally weaker than a
  fleet-wide schema-resolution story) is not addressed by this ADR and
  was not the direction chosen.
- **A DeltaZulu.Pipeline-side reimplementation of MessagePack encoding**:
  rejected in favor of using `DeltaZulu.Forward`'s own `ForwardLogBatchCodec`
  — duplicating an already-owned, already-tested codec would be redundant
  and would risk drifting from the upstream library's normalization rules.

## Related decisions

- Narrows [ADR 0010](0010-type-catalog-avro-arrow-and-ndjson-edge-dialect.md)
  to its type-contract-catalog authority; supersedes its Avro-wire clause.
- Corrects the wire-format description in
  [ADR 0011](0011-deltazulu-forward-transport.md).
- Reopens (does not resolve) the payload-format assumption in
  [ADR 0012](0012-proton-ingestion-intermediate-protocol.md)'s
  Kafka-API-compatible intermediate.
