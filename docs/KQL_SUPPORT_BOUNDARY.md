# Agent KQL support boundary

DeltaZulu.Agent executes KQL with `Microsoft.Rx.Kql` over live `IObservable`
source rows. The Agent does not maintain a second per-operator KQL capability
blocklist: KQL capability is determined by the pinned Rx.Kql engine and the
Agent compatibility layer.

## Terminology

- **RxKqlSupported**: the pinned `Microsoft.Rx.Kql` package can parse and
  execute the syntax for the live observable query model.
- **AgentCompatible**: the Agent provides a compatibility shim or scalar helper
  in addition to the engine's support.
- **InteractiveQueryAllowed**: a CLI or workbench query accepted by its local UX
  validation and executable by Rx.Kql.

There is intentionally no distinct Agent-maintained per-operator production
support list. A profile must be executable by Rx.Kql in the same way as an
interactive query; host-specific resource/profile validation remains separate
from KQL capability.

## Agent compatibility layer

The executor rewrites a declared profile input table name to its runtime
observable name, normalizes `notin` to `!in`, expands `has_any (...)`, and
registers Agent scalar helpers: `strcat_array`, `isnotempty`, `ntohs`, and
`getprocessname`. These shims are documented and tested Agent compatibility,
not a claim of full Azure Data Explorer compatibility.

The versioned, executable feature classification is published in
[Rx.Kql capability matrix](RXKQL_CAPABILITY_MATRIX.md). Unsupported entries in
that matrix are engine limitations, not an Agent-maintained deny list.

## Architecture and ADR 0004

Input adapters collect and parse source-native events. Profiles select resources
and run their KQL queries over those events. The Agent preserves the delivery
metadata envelope and performs deterministic Agent-owned enrichment separately;
DeltaZulu.Platform remains authoritative for semantic normalization and canonical
mapping. This architectural guidance is described in
[ADR 0004](adr/0004-kql-capability-alignment.md).

Profile authors should use source-preserving predicates for forwarding whenever
possible. Projection and display-oriented queries are appropriate for interactive
CLI/workbench results, which are not forwarded as source telemetry. The executor
does not reinterpret Rx.Kql operator support as a separate policy list.

## Interactive CLI and workbench

Ad hoc CLI tail and workbench queries run against the same Rx.Kql executor and
can use any RxKqlSupported and AgentCompatible operation that their query
validation permits, including `project`. Workbench validation rejects empty or
multi-statement queries, `render`, and a query whose first table does not match
the selected source; those are UX/safety constraints, not a general KQL operator
capability matrix.
