# ADR 0002: Windows SID Observation Boundary

## Status

Accepted

## Context

Windows Security events can contain volatile SIDs for short-lived local accounts. The Agent may see these SIDs while the account still exists, but Platform-side enrichment can happen after the account has already been deleted. The Agent therefore needs to capture SID-to-name evidence at first sight without becoming the owner of final identity normalization, severity classification, or vendor-specific suppression.

KQL can express joins in environments that provide multiple input tables. Platform KQL is therefore the right place to join Security, Sysmon, ETW, SID-observation, and process-observation tables by durable keys such as SID or device/boot/process identifiers. The Agent profile KQL layer, however, is intentionally a per-resource filtering and projection layer over the current source observable. It is not a durable, stateful enrichment engine, and it should not be responsible for account or process lifecycle memory across Agent restarts.

## Decision

The Agent enrichment layer emits SID resolution facts as first-class `Sid` table observations. Security, Sysmon, and ETW records continue to preserve raw SID fields and can be joined to the `Sid` table by SID in Platform detection logic. For example, Platform KQL can use the event stream as the left side and join the `Sid` table on `TargetUserSid == Sid` or `SubjectUserSid == Sid`, with time-windowing when needed.

The same observation-table pattern applies to process context. When an Agent can observe process identity, image, and command-line facts, enrichment should emit them as `ProcessObservation` records rather than rewriting source events into a single locally enriched shape. Platform KQL can then join source records to the process table by tenant/device/boot plus process id and event time. This keeps command-line capture optional and source-aware while still allowing detections to enrich events with process names or command lines.

Configured Windows Security profiles declare SID-bearing and process-id-bearing source fields in profile KQL projections instead of a separate YAML field-mapping block. The enrichment code discovers projected `*Sid` fields and their projected companion name/domain fields, using source metadata to decide what can be observed. However, the actual SID cache lookup, local resolution, durable persistence, and deletion lifecycle update run in the enrichment library around profile KQL filtering/projection; profile KQL is the declaration surface, not the durable cache.

Platform KQL may join against the `Sid` and `ProcessObservation` tables for detection. Agent profile KQL must not be the primary mechanism for SID resolution, durable caching, process-id-to-process-name joining, command-line backfilling, lifecycle tracking, severity downgrade, or vendor classification.

## Consequences

- Platform receives a separate `Sid` stream/table of SID resolution observations keyed by SID.
- Platform can also receive a separate process observation table keyed by tenant/device/boot/process id and time.
- Raw event fields are unchanged, so Platform rules can use SID or process identifiers as primary correlation keys.
- Resolved usernames are available as classification and severity modifiers when resolution succeeded.
- Username-only matching is not required for transient local-admin lifecycle detection.
- Agent profiles expose SID-bearing and process-id-bearing fields through KQL projection instead of parallel YAML field lists.
- Platform KQL can join event tables to the `Sid` table by SID and to the process table by process identity.
- Agent profile KQL remains stateless and does not need joins against a local durable SID cache or process cache.
