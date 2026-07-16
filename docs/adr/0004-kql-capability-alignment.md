# ADR 0004: KQL capability alignment and source event guidance

## Status

Accepted.

Supersedes the KQL capability restriction in [ADR 0003: Profile KQL Preserves
Source Event Shape](0003-profile-kql-preserves-source-event-shape.md).

## Context

ADR 0003 correctly identified the risk of treating profile KQL as an implicit
normalization language: `project` can drop source fields and obscure provider
variation. Its decision enforced that concern by prohibiting `project` in
checked-in profiles.

The Agent executes KQL through `Microsoft.Rx.Kql` over live observables and also
provides a small compatibility layer for table aliases, syntax normalization,
and Agent scalar helpers. Maintaining a separate Agent operator blocklist creates
a second, incomplete and potentially divergent capability model. It is not an
accurate description of what Rx.Kql can execute.

## Decision

KQL capability is the union of **RxKqlSupported** operations in the pinned
`Microsoft.Rx.Kql` package and documented **AgentCompatible** shims/helpers. The
Agent does not maintain a separate per-operator production-profile blocklist.
A profile query and an interactive query use the same executor capability; host
validation continues to validate profile structure, resource contracts, and
non-KQL safety/UX constraints.

This supersedes only the former KQL operator prohibition. It does not make the
Agent the canonical semantic-normalization layer:

- input adapters remain responsible for collecting and parsing source-native
  records;
- `_metadata` remains Agent-owned delivery metadata rather than a user field;
- deterministic Agent-owned enrichment remains a separate pipeline concern; and
- DeltaZulu.Platform remains authoritative for semantic normalization and
  canonical mapping.

Profile authors should prefer source-preserving predicates when forwarding raw
telemetry. When a query deliberately produces a projection or derived result,
that result is the query output. Source raw-capture metadata and the shape of a
query result are distinct provenance concerns and must not be conflated.

## Consequences

- Rx.Kql upgrades and executor tests define executable KQL capability rather
  than an Agent-maintained operator table.
- CLI/workbench and profile execution no longer diverge due to an unverified
  operator policy.
- Documentation must distinguish engine support, Agent compatibility shims, and
  source-event provenance rather than conflating them.
- Any future policy that restricts a query for privacy, regulation, performance,
  or delivery reasons requires a concrete requirement and a separate ADR; it is
  not implied by KQL syntax alone.
