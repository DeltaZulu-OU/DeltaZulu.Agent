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

The collector has no columnar/Arrow in-memory representation. It decodes
each MessagePack `ForwardLogBatch` record, validates and types its fields
against the type-contract catalog (KQL scalar, logical annotation,
nullability, unit — ADR 0010's catalog authority, unchanged), and works
directly with that catalog-typed record for every downstream consumer:
the Proton sink (ADR 0012/0016) and the DuckDB leg both ingest from
catalog-typed records, not from an Arrow `RecordBatch`.

The catalog's generated-projection list narrows again: Proton DDL, DuckDB
DDL, and parser contracts. There is no generated Arrow in-memory schema
projection, in addition to no Avro wire schema projection (ADR 0014).

DuckDB ingestion is an explicit per-batch conversion from catalog-typed
records (for example, via DuckDB's Appender API operating on application-
level values) rather than a zero-copy Arrow handoff. This is a deliberate,
accepted cost — see Consequences.

## Consequences

- Loses Arrow's zero-copy DuckDB ingest path and Arrow-native handling on
  any future Proton leg that might have read Arrow directly. DuckDB
  ingestion becomes an explicit conversion step; its throughput against
  catalog-typed records must be benchmarked against the NRT and
  retrospective-hunting budgets rather than assumed from Arrow's zero-copy
  reputation.
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
