# ADR 0010: Type-contract catalog, Avro wire, Arrow server batches, and governed NDJSON edges

## Status

Accepted

## Context

DeltaZulu's ingestion path crosses several incompatible type systems:
liblognorm-derived parser fields, JSON/NDJSON transport, Rx.Kql/KQL scalar
values, Timeplus Proton streaming tables, and DuckDB analytical tables. The
documented intersection of those systems is smaller than the values DeltaZulu
must preserve: timestamps with precision and timezone semantics, durations,
network identifiers, UUIDs, large integers, decimals, nested structures, and
null-vs-absent distinctions do not survive a convention-only NDJSON wire
contract.

The target architecture also fans out to Proton for near-real-time detection and
DuckDB for retrospective hunting. If each sink reconstructs physical types from
NDJSON independently, the same logical field can become different physical types
in each backend. KQL translation then becomes backend-specific by accident, and
the same query can return different results because of type drift rather than
source data.

## Decision

DeltaZulu will introduce a producer-agnostic, machine-readable **type-contract
catalog** as the single type authority for parsed and structured events. Per
source, per field, the catalog records the emitted name, KQL scalar (the
author-facing contract), logical annotation (the translator-facing contract
for semantics the KQL scalar set doesn't carry: `ipv4`, `ipv6`, `mac48`,
duration units, `decimal`, `guid`, nested paths), parser grammar reference,
canonicalization policy, promotion flag, and a reserved (initially empty)
semantic column for the deferred OTel-vocabulary layer (ADR 0005). It is not
keyed to a particular parser's output; XML, CSV, JSON, native Windows, and
future structured producers must pass through the same catalog checkpoint.
The catalog is deliberately not a semantic schema: it normalizes
representation, not meaning.

Five projections generate from the catalog: the Avro wire schema, the Arrow
in-memory schema, Proton DDL, DuckDB DDL, and parser contracts.

Agent-to-collector transport uses Avro schemas generated from the catalog,
carried over DeltaZulu.Forward (ADR 0011). Agents cache schemas locally, spool
Avro records during catalog or network outages, and fail visibly on schema
rejection. They do not silently fall back to NDJSON for type-bearing
transport.

The collector decodes Avro exactly once into Arrow record batches. Arrow is
the collector's internal typed batch representation. The DuckDB leg ingests
from Arrow where practical; the Proton leg is served through a Kafka-API-
compatible intermediate protocol per ADR 0012, not a bespoke native-protocol
sink.

NDJSON remains only a governed edge dialect for third-party JSON ingress,
customer/API egress, operator debug taps, and dead-letter/error envelopes. Those
projections are catalog-driven and must not become an alternate internal wire
format.

## Type invariants

- Event timestamps are UTC `timestamp-micros` on the Avro wire and
  `timestamp(us, UTC)` in Arrow.
- Timestamp sink mappings are generated deliberately, for example DuckDB
  `TIMESTAMPTZ` and Proton `datetime64` with the chosen precision and UTC policy.
- Durations carry an explicit logical unit in the catalog; translators must not
  infer seconds, milliseconds, or ticks from field names.
- Large integers and decimals use typed Avro/Arrow carriers, not JSON doubles.
- UUID, IP, MAC, enum, nested, JSON-like, variant, and geo fields require
  explicit logical annotations and per-backend mappings.
- Null, absent, and empty-string semantics are part of the catalog and query
  translator contract.

## Consequences

The type-contract catalog becomes a prerequisite for claiming Proton/DuckDB
answer parity for a KQL query. DDL, Avro schemas, Arrow schemas, translator
type tables, and edge JSON projections are generated from one source instead
of hand-maintained independently.

The design favors mixed-version fleet evolution over human-readable internal
transport. Operational readability is preserved through the governed debug tap
and dead-letter JSON envelope rather than by retaining NDJSON as the production
wire.

Proton's Kafka-API external-stream ingestion path is verified as available in
the target OSS version (ADR 0012), which closes this ADR's former open
verification in the affirmative at the feature level. Whether its Avro
handling honors the catalog's logical types directly, or requires explicit
column declaration against raw Avro, remains a Phase 3b integration-testing
question, not a documentation read.

## Rejected alternatives

- **NDJSON as the internal type-bearing wire:** rejected because it collapses to
  the RFC 8259 value model and requires independent reconstruction at Rx.Kql,
  Proton, and DuckDB.
- **Degraded-mode NDJSON fallback:** rejected because every consumer would need
  to support two internal formats indefinitely and the fallback would reintroduce
  silent type drift exactly during failures.
- **MessagePack or CBOR:** rejected because self-describing tags do not provide a
  catalog authority, sink DDL, or query-translation contract.
- **Protobuf:** rejected for this pipeline because decimal and schema-evolution
  ergonomics do not match detection-content velocity, and DuckDB does not gain a
  native ingest advantage.
- **Arrow as the agent wire:** rejected for mixed-version fleet transport because
  schema evolution is batch-local and operationally weaker than Avro writer-reader
  resolution.

## Related decisions

- ADR 0011 names and designs the Avro-carrying transport (DeltaZulu.Forward).
- ADR 0012 resolves the Proton ingestion mechanism (Kafka-API-compatible
  intermediate; no bespoke sink), replacing this ADR's original "native
  protocol or another verified OSS-supported path" language.
