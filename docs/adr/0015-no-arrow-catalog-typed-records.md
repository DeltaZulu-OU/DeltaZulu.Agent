# ADR 0015: No Arrow — catalog-typed records are the collector's internal representation

## Status

Accepted. Supersedes the internal-representation choice in
[ADR 0010](0010-type-catalog-avro-arrow-and-ndjson-edge-dialect.md).

## Context

ADR 0010 decided Arrow as the collector's internal typed-batch
representation: the catalog generated an Arrow in-memory schema, the
collector decoded the wire batch exactly once into Arrow record batches,
and DuckDB ingested from Arrow "where practical" (zero-copy). Like Avro
(superseded by [ADR 0014](0014-messagepack-wire-supersedes-avro.md)), Arrow
was recorded as though it were a final decision. It was an alternative
under consideration, not a decision this project is bound to. DeltaZulu
does not want an Arrow layer at all, on the collector or anywhere else in
the pipeline.

With ADR 0014 already in place, the wire format is MessagePack
(`ForwardLogBatch`), decoded and normalized to the catalog's KQL-scalar set
by `ForwardValueNormalizer`. Introducing Arrow after that point would add a
second typed representation between the wire and the sinks, for no purpose
this project has decided it needs.

## Decision

The collector has no columnar/Arrow in-memory representation and no shared
intermediate format between sinks. It decodes each MessagePack
`ForwardLogBatch` record, validates and types its fields against the
type-contract catalog (KQL scalar, logical annotation, nullability, unit —
ADR 0010's catalog authority, unchanged), and hands that single
catalog-typed record to a **dedicated per-backend adapter** for each sink:

- A **DuckDB adapter** converts the catalog-typed record into calls against
  DuckDB's Appender API (or equivalent bulk-insert primitive), operating on
  application-level values rather than a zero-copy Arrow handoff.
- A **Proton adapter** converts the catalog-typed record into the payload
  the bespoke native Proton sink (ADR 0016) publishes.

Each adapter is a direct, owned conversion from the catalog-typed record to
that backend's native ingestion primitive — there is no generic/Arrow-like
layer either adapter reads from. The catalog's DuckDB DDL and Proton DDL
projections (ADR 0010) remain each adapter's schema authority.

The catalog's generated-projection list narrows again: Proton DDL, DuckDB
DDL, and parser contracts. There is no generated Arrow in-memory schema
projection, in addition to no Avro wire schema projection (ADR 0014).

## Consequences

- Loses Arrow's zero-copy DuckDB ingest path. DuckDB ingestion is an
  explicit adapter conversion step instead; its throughput against
  catalog-typed records is benchmarked against the NRT and
  retrospective-hunting budgets (ROADMAP.md Phase 3b/18) rather than
  assumed from Arrow's zero-copy reputation — the mechanism (a DuckDB
  adapter) is decided here, only its measured performance is deferred.
- Two owned adapters (DuckDB, Proton) now exist where a single shared Arrow
  representation would have served both; each needs its own conversion
  correctness tests against the catalog's KQL-scalar/logical-annotation
  model, in addition to the bespoke Proton sink's own protocol-compatibility
  testing (ADR 0016).
- ADR 0010's type-contract-catalog authority (KQL scalars, logical
  annotations, nullability, units, per-backend physical mappings) is
  unaffected and remains the single type authority; only the Arrow
  in-memory-representation clause is superseded.
- [ADR 0014](0014-messagepack-wire-supersedes-avro.md)'s own text ("Arrow
  remains the collector's internal typed batch representation, unchanged
  from ADR 0010") is corrected by this ADR: Arrow is no longer the
  collector's internal representation either.
- No source code in this repository referenced Arrow at the time of this
  ADR (verified by search); this is a pre-implementation documentation
  correction, not a migration.

## Alternatives rejected

- **Arrow** (ADR 0010's original decision): rejected per this ADR's own
  premise — DeltaZulu does not want an Arrow layer. Arrow's zero-copy
  columnar batch model is not needed once the collector works directly
  with catalog-typed records end to end.

## Related decisions

- Narrows [ADR 0010](0010-type-catalog-avro-arrow-and-ndjson-edge-dialect.md)
  further, alongside ADR 0014; the catalog's type authority is unaffected,
  only its Arrow projection is superseded.
- Corrects [ADR 0014](0014-messagepack-wire-supersedes-avro.md)'s statement
  that Arrow remains the collector's internal representation.
- Removes the Arrow assumption underlying the Proton ingestion mechanism
  superseded by [ADR 0016](0016-bespoke-proton-sink-supersedes-kafka-intermediate.md).
