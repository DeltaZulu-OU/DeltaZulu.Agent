# ADR 0016: A bespoke native Proton sink supersedes the Kafka-API-compatible intermediate

## Status

Accepted. Supersedes [ADR 0012](0012-proton-ingestion-intermediate-protocol.md)'s decision in full.

## Context

ADR 0012 chose a Kafka-API-compatible intermediate (Redpanda or an embedded
Kafka-protocol broker, with a Python external-stream plugin as fallback)
specifically to avoid writing a bespoke Proton sink, which it rejected for
stated reasons: protocol versioning tracks upstream Proton releases,
block-format details are internal rather than contractual, and the sink
would sit on the latency-critical near-real-time (NRT) detection path,
where its bugs become detection-availability incidents. That rejection was
recorded as a final decision; it was an alternative under consideration,
not a decision this project is bound to. DeltaZulu does not want a Kafka
integration on this leg at all — no broker, no Kafka-API external-stream
dependency, no Python transport-shim fallback.

This ADR does not dispute ADR 0012's description of the bespoke-sink costs
— they are real and are accepted here, not dismissed.

## Decision

DeltaZulu writes a bespoke Proton output sink against Proton's native
(ClickHouse-lineage) ingestion protocol. There is no Kafka-API-compatible
broker in the deployment path (no Redpanda, no embedded Kafka-protocol
endpoint) and no Python external-stream fallback plugin. The sink includes
the Proton adapter named in [ADR 0015](0015-no-arrow-catalog-typed-records.md):
a direct conversion from the catalog-typed record (ADR 0010's type
authority, ADR 0015's no-Arrow internal representation) to the payload
this sink publishes to Proton.

The catalog's Proton-DDL projection (ADR 0010) remains the type authority
for the sink's target schema, now consumed directly by the bespoke sink
instead of being used to declare Kafka external-stream columns.

End-to-end dedup responsibility remains at the collector, before
publication to Proton (ADR 0011's dedup-window discussion is unaffected by
this change — only the publication mechanism changes, not where dedup
happens).

## Consequences

Positive: one fewer deployed component — no broker, no broker operational
surface (disk, retention, monitoring) in the deployment story, no added
network hop and therefore no broker-hop latency budget to measure against
the NRT path.

Negative and accepted — this is the cost ADR 0012 originally described and
this ADR now accepts rather than avoids:

- Protocol versioning tracks upstream Proton releases; a Proton upgrade can
  change the native ingestion protocol without the same contractual
  stability a documented Kafka-API external-stream surface would have
  offered.
- Block-format and native-protocol details are internal to Proton rather
  than contractual; the sink's correctness depends on tracking
  undocumented or loosely documented upstream behavior.
- The sink sits on the latency-critical NRT detection path; its bugs become
  detection-availability incidents, not merely ingestion delays absorbed by
  a broker.
- This is now an owned implementation, alongside Parse, the catalog, and
  DeltaZulu.Forward, and needs its own test harness, versioning discipline,
  and upgrade-compatibility testing against tracked Proton releases before
  it can claim production readiness.
- The "buy boring integration where it is free" principle ADR 0012 applied
  to the Kafka-API path no longer applies to this leg; DeltaZulu now owns
  the Proton integration surface directly.

## Alternatives rejected

- **Kafka-API-compatible intermediate** (ADR 0012's original decision):
  rejected per this ADR's own premise — DeltaZulu does not want a Kafka
  integration on this leg.
- **Python external-stream input plugin** (ADR 0012's fallback path):
  rejected for the same reason; it depended on the same Kafka-API-family
  Timeplus ingestion surface this ADR moves away from.
- **REST ingest API**: still rejected for the reason ADR 0012 gave —
  documented as a Timeplus Enterprise feature, and request-per-batch HTTP
  semantics fit the NRT path poorly.
- **Direct file/S3 staging**: still rejected — batch-oriented, wrong
  latency class for the detection leg.

## Revisit triggers

Reopen if the bespoke sink's maintenance cost (tracking Proton native
protocol changes across releases) proves unsustainable in practice, or if a
Proton upgrade breaks native-protocol compatibility in a way that
threatens the NRT path's availability; a Kafka-API-compatible intermediate
would be the first alternative to reconsider if either trigger fires.

## Related decisions

- Fully supersedes [ADR 0012](0012-proton-ingestion-intermediate-protocol.md).
- Consumes [ADR 0010](0010-type-catalog-avro-arrow-and-ndjson-edge-dialect.md)'s
  catalog authority and Proton-DDL projection directly, per
  [ADR 0015](0015-no-arrow-catalog-typed-records.md)'s no-Arrow internal
  representation.
