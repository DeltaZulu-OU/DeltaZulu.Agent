# ADR 0012: Proton ingestion — intermediate protocol, no bespoke sink

## Status

Accepted — 2026-07-19. Target design; not yet implemented (see ROADMAP.md
Phase 3b).

## Context

ADR 0010 left the Proton leg with an open verification (native Avro ingest
availability in the OSS edition) and an implicit assumption that the collector
would eventually implement a Proton output sink against the native
ClickHouse-lineage protocol. Writing and maintaining a bespoke sink against
that protocol is a materially larger commitment than it appears: protocol
versioning tracks upstream releases, block-format details are internal rather
than contractual, and the sink would sit on the latency-critical
near-real-time (NRT) detection path, where its bugs become detection-
availability incidents.

## Decision

DeltaZulu writes no Proton output sink. The collector publishes typed Avro
batches to an intermediate protocol that Proton consumes through its own
documented, vendor-maintained ingestion surface. Two sanctioned paths, in
order of preference:

- **Primary — Kafka-API-compatible intermediate.** Timeplus documents
  external streams for Apache Kafka, Confluent, Redpanda, and other
  Kafka-API-compatible platforms as available in Timeplus Proton, which closes
  the former OSS-edition verification in the affirmative for this path. The
  collector publishes to a lightweight Kafka-API-compatible broker (Redpanda
  single-binary, or an embedded Kafka-protocol endpoint, selected at
  deployment scale); Proton reads via an external stream. Avro is a
  first-class payload format on Kafka-protocol infrastructure, so the wire
  format (ADR 0010/0011) and the Proton intermediate speak the same
  serialization without re-encoding.
- **Fallback — Python external stream input plugin.** Where deploying a
  broker is unwanted (minimal single-node deployments), a trivial
  Python-based input plugin reads from the collector's output (local queue or
  socket) and feeds Proton directly through Timeplus's documented Python
  external stream source. The plugin is a transport shim only — no parsing,
  no typing — so it introduces no third reconstruction; types are established
  by the Avro schema and the catalog-generated Proton DDL.

This applies the "buy boring integration where it is free" principle: the
Proton integration seam is exactly the kind a commodity protocol (Kafka API)
already covers, so DeltaZulu buys it instead of owning it. Owned
implementations (Parse, the catalog, DeltaZulu.Forward per ADR 0011) are
reserved for components where ownership purchases type correctness or
fidelity.

End-to-end dedup responsibility is located at the collector, before
publication to the intermediate broker — the broker consumes an
already-deduplicated stream (see ADR 0011's dedup-window discussion).

## Alternatives rejected

- **Bespoke native-protocol sink**: maximum control and minimum moving parts
  at runtime, rejected for the maintenance liability described above.
- **REST ingest API**: documented as a Timeplus Enterprise feature;
  request-per-batch HTTP semantics fit the NRT path poorly.
- **Direct file/S3 staging**: batch-oriented, wrong latency class for the
  detection leg.

## Consequences

Positive: the Proton integration is maintained by Timeplus against a protocol
(Kafka API) with massive ecosystem investment; the collector's Proton-side
code is a standard producer client plus catalog-driven topic/schema wiring;
the intermediate broker doubles as a natural replay and buffering point for
the NRT path, decoupling collector availability from Proton availability.

Negative and accepted: one more deployed component in the primary path
(mitigated by single-binary broker options and the Python fallback for
minimal deployments); broker operational surface (disk, retention,
monitoring) enters the deployment story; end-to-end latency gains one hop, to
be measured against the NRT budget during Phase 5 verification.

Open detail: whether Proton's external-stream Avro handling honors the
catalog's logical types directly, or requires the external stream to declare
explicit column types against raw Avro, is resolved during Phase 3b
integration testing; the catalog's Proton-DDL projection is updated to
whichever declaration form the external stream requires.

## Revisit triggers

Reopen only if: measured broker-hop latency breaches the NRT detection
budget; the external-stream path proves lossy for catalog types in a way DDL
declaration cannot resolve; or deployment feedback shows the broker component
blocking adoption in the deployment classes that matter commercially.
