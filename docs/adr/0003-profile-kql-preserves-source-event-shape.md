# ADR 0003: Profile KQL Preserves Source Event Shape

## Status

Superseded by [ADR 0004: KQL capability alignment and source event guidance](0004-kql-capability-alignment.md).

> Historical decision: this ADR originally prohibited `project` in checked-in
> profiles. That restriction is no longer the Agent KQL capability policy.
> The superseding ADR retains source-event and Platform-normalization guidance
> without maintaining an Agent-specific per-operator blocklist.

## Context

DeltaZulu resource profiles use KQL to decide which source events should continue
through the pipeline. Earlier profiles also used trailing `project` operators to
shape each resource into a fixed field list. That made profiles convenient as a
field declaration surface, but it also meant a profile could accidentally remove
raw source fields, emit empty columns for fields the provider did not expose, or
hide provider/event-version differences that are important for downstream
forensics.

The output configuration can still request raw-event preservation and metadata
envelopes, but a KQL `project` operator changes the event payload before the
record is forwarded. That conflicts with the goal that profiles should filter
logs, not amend the source event shape.

## Decision

Profile KQL must not use `project` to amend, normalize, or restrict log results.
Profiles should express source/resource selection with predicates such as
`where`, while leaving the original event field set intact for forwarding and
platform-side detection.

Field selection, normalization, schema mapping, enrichment joins, and display
column choices belong outside profile KQL. They should be handled by explicit
pipeline components, observation streams, platform-side KQL, or user-invoked CLI
queries where the caller intentionally asks for a projected view.

This rule applies to checked-in resource profiles. Ad hoc CLI KQL remains allowed
to use `project` because it is an interactive presentation/query concern rather
than a profile contract for forwarded telemetry.

## Consequences

- Checked-in profiles preserve the raw event fields emitted by their source
  adapters after filtering.
- Profiles no longer fabricate sparse output shapes where projected fields appear
  as null or empty only because a specific provider event did not expose them.
- Detection and normalization logic must tolerate source-specific field names and
  perform schema mapping downstream from profile filtering.
- Agent-side enrichment that needs durable state should emit separate observation
  records instead of rewriting the source event into a locally enriched shape.
- Tests should reject `| project` in checked-in profile queries so future profile
  changes do not silently reintroduce field-shaping behavior.
