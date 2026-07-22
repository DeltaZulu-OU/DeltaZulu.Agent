# ADR 0005: Single Pipeline assembly and internal boundaries

## Status

Accepted.

## Context

Earlier plans proposed separate Pipeline Core, Inputs, Outputs, Enrichment, and
Tunnel assemblies. The repository now has one multi-targeted
`DeltaZulu.Pipeline` assembly. Splitting it now would increase dependency and
release complexity before its internal contracts have stabilized.

## Decision

Keep `DeltaZulu.Pipeline` as one assembly. Enforce boundaries with folders,
namespaces, interfaces, internal visibility, and architecture tests. Pipeline may
depend on Parse, LocalStream, Forward, deterministic codecs, and approved native
eventing libraries; it must not depend on Agent Runtime, Daemon, CLI,
ProfileWorkbench, or Filter. Runtime owns orchestration and Filter owns Rx.Kql.

Pipeline extraction is deferred until these boundaries are stable; it must not
recreate component-level Pipeline assemblies by default.

## Consequences

- New Pipeline features use the logical modules documented in Architecture.
- Architecture tests prevent Agent-project references and identify transitional
  direct DurableBuffer uses.
- A direct DurableBuffer dependency is removed after LocalStream migration.
  LocalStream is a primitive in-agent stream substrate, not a DurableBuffer
  wrapper or facade.
