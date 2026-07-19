# ADR 0010: Schema registry, Avro wire, Arrow server batches, and governed NDJSON edges

## Status

Accepted

## Context

DeltaZulu's ingestion path crosses several incompatible type systems:
liblognorm parser fields, JSON/NDJSON transport, Rx.Kql/KQL scalar values,
Timeplus Proton streaming tables, and DuckDB analytical tables. The documented
intersection of those systems is smaller than the values DeltaZulu must preserve:
timestamps with precision and timezone semantics, durations, network
identifiers, UUIDs, large integers, decimals, nested structures, and null-vs-
absent distinctions do not survive a convention-only NDJSON wire contract.

The target architecture also fans out to Proton for near-real-time detection and
DuckDB for retrospective hunting. If each sink reconstructs physical types from
NDJSON independently, the same logical field can become different physical types
in each backend. KQL translation then becomes backend-specific by accident, and
the same query can return different results because of type drift rather than
source data.

## Decision

DeltaZulu will introduce a producer-agnostic schema registry as the single type
authority for parsed and structured events. The registry defines each field's
logical type, nullability, precision, unit, canonical text form where required,
and backend physical mappings. It is not keyed to liblognorm parser output; XML,
CSV, JSON, native Windows, and future structured producers must pass through the
same registry checkpoint.

Agent-to-server transport will use Avro schemas generated from the registry.
Agents cache schemas locally, spool Avro records during registry or network
outages, and fail visibly on schema rejection. They do not silently fall back to
NDJSON for type-bearing transport.

The server decodes Avro exactly once into Arrow record batches. Arrow is the
server's internal typed batch representation. The DuckDB leg ingests from Arrow
where practical; the Proton leg writes through the native protocol or another
verified OSS-supported path generated from the same registry mapping.

NDJSON remains only a governed edge dialect for third-party JSON ingress,
customer/API egress, operator debug taps, and dead-letter/error envelopes. Those
projections are registry-driven and must not become an alternate internal wire
format.

## Type invariants

- Event timestamps are UTC `timestamp-micros` on the Avro wire and
  `timestamp(us, UTC)` in Arrow.
- Timestamp sink mappings are generated deliberately, for example DuckDB
  `TIMESTAMPTZ` and Proton `datetime64` with the chosen precision and UTC policy.
- Durations carry an explicit logical unit in the registry; translators must not
  infer seconds, milliseconds, or ticks from field names.
- Large integers and decimals use typed Avro/Arrow carriers, not JSON doubles.
- UUID, IP, MAC, enum, nested, JSON-like, variant, and geo fields require
  explicit logical annotations and per-backend mappings.
- Null, absent, and empty-string semantics are part of the registry and query
  translator contract.

## Consequences

The schema registry becomes a prerequisite for claiming Proton/DuckDB answer
parity for a KQL query. DDL, Avro schemas, Arrow schemas, translator type tables,
and edge JSON projections are generated from one source instead of hand-maintained
independently.

The design favors mixed-version fleet evolution over human-readable internal
transport. Operational readability is preserved through the governed debug tap
and dead-letter JSON envelope rather than by retaining NDJSON as the production
wire.

Proton native JSON support and schema-registry ingest capabilities remain
implementation verifications, not assumptions. The architecture does not depend
on Enterprise-gated Proton features: if a feature is not verified in the target
OSS version, the generated Proton writer must use the native protocol and an
explicit physical schema.

## Rejected alternatives

- **NDJSON as the internal type-bearing wire:** rejected because it collapses to
  the RFC 8259 value model and requires independent reconstruction at Rx.Kql,
  Proton, and DuckDB.
- **Degraded-mode NDJSON fallback:** rejected because every consumer would need
  to support two internal formats indefinitely and the fallback would reintroduce
  silent type drift exactly during failures.
- **MessagePack or CBOR:** rejected because self-describing tags do not provide a
  schema authority, sink DDL, or query-translation contract.
- **Protobuf:** rejected for this pipeline because decimal and schema-evolution
  ergonomics do not match detection-content velocity, and DuckDB does not gain a
  native ingest advantage.
- **Arrow as the agent wire:** rejected for mixed-version fleet transport because
  schema evolution is batch-local and operationally weaker than Avro writer-reader
  resolution.
