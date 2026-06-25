# Log Utilization Framework: Agent-side requirements

## Boundary

The agent only emits structured observations about collection behavior. It must not compute authoritative utilization state, ruleset utilization, storage utilization, or deterministic suggestions. Those responsibilities remain server-side.

Agent responsibilities are limited to observable pipeline facts:

```text
read -> kept after local filtering -> forwarded or failed forwarding
```

The agent should also report source health facts that help the server distinguish input gaps from source permission, channel, bookmark, or read failures.

## Required first-version agent deliverables

1. Define a stable `LogKey` representation for observation grouping.
2. Emit `collector.pipeline.counts` records by `LogKey` and aggregation window.
3. Emit `collector.source.health` records for configured sources.
4. Emit `collector.filter.summary` records by filter/profile and aggregation window.
5. Track forwarding success and forwarding failure counts at the output boundary.
6. Preserve profile, host, agent, source, filter, and window metadata on every observation.

## LogKey requirements

The canonical telemetry identity is:

```text
LogKey = SourceType + Channel + Provider + EventId
```

For the initial Windows Event Log implementation, the minimum practical key is:

```text
WindowsEventLog + Channel + EventId
```

The agent must not group observations by `EventId` alone because event identifiers are not globally unique across providers, channels, or source types.

Recommended fields for Windows Event Log observations:

| Field | Required | Notes |
| --- | --- | --- |
| `sourceType` | Yes | Use `WindowsEventLog`. |
| `channel` | Yes | Example: `Security`. |
| `provider` | Preferred | Example: `Microsoft-Windows-Security-Auditing`; nullable only when unavailable. |
| `eventId` | Yes | Numeric Windows event ID. |

## Observation metadata requirements

Every count observation should include:

| Field | Meaning |
| --- | --- |
| `agentId` | Stable agent identity. |
| `hostId` | Stable endpoint/host identity when available. |
| `profileId` | Active collection profile. |
| `observedAt` | Time the observation record was emitted. |
| `windowStart` | Inclusive aggregation window start. |
| `windowEnd` | Exclusive aggregation window end. |

Filter-scoped records should also include `filterId` when the running profile has a distinct filter identity.

## Pipeline count observation

Emit one `collector.pipeline.counts` record per `LogKey` per window.

Required body fields:

| Field | Meaning |
| --- | --- |
| `sourceType` | Source family, such as `WindowsEventLog`. |
| `channel` | Source channel or stream name. |
| `provider` | Provider name when available. |
| `eventId` | Event identifier when available. |
| `readCount` | Number of events read into the agent pipeline. |
| `keptAfterFilterCount` | Number of events that survived local filtering. |
| `discardedCount` | Number of events read but discarded locally. |
| `forwardedCount` | Number of events emitted to the sink/spool/relay boundary. |
| `forwardFailedCount` | Number of events that failed forwarding. |

`keptAfterFilterCount` must be used instead of `retainedCount`; retention is a server/storage concept.

Count consistency target:

```text
readCount = keptAfterFilterCount + discardedCount
keptAfterFilterCount = forwardedCount + forwardFailedCount + pendingForwardCount
```

`pendingForwardCount` is optional for the first version, but should be added if asynchronous buffering makes forwarded versus failed status unavailable by the end of the observation window.

## Source health observation

Emit `collector.source.health` for each configured source/channel. These records let the server identify whether missing observations are caused by unreadable sources rather than filter or forwarding behavior.

Required body fields:

| Field | Meaning |
| --- | --- |
| `sourceType` | Source family, such as `WindowsEventLog`. |
| `channel` | Source channel or stream name. |
| `isEnabled` | Whether the source/channel appears enabled or configured. |
| `canRead` | Whether the agent can currently read it. |
| `lastReadAt` | Last successful source read timestamp, nullable. |
| `readErrorCount` | Count of source read errors in the health interval. |
| `lastError` | Last source, bookmark, permission, or read error, nullable. |

For Windows Event Log, health checks should distinguish at least channel missing/disabled, permission denied, bookmark/cursor errors, and generic read failures when that detail is available.

## Filter summary observation

Emit `collector.filter.summary` once per filter/profile/source/channel/window. This is a coarse summary used to evaluate filter quality before server-side joins to usage metadata.

Required body fields:

| Field | Meaning |
| --- | --- |
| `sourceType` | Source family. |
| `channel` | Source channel or stream name. |
| `readCount` | Total events read before filtering. |
| `keptAfterFilterCount` | Total events kept after filtering. |
| `discardedCount` | Total events discarded by filtering. |
| `forwardedCount` | Total events emitted to the output boundary. |

## Forwarding and buffer instrumentation needs

The utilization plan requires the agent to report what crossed the agent output boundary, not what was eventually stored centrally. Therefore:

- A record should count as forwarded when it is successfully emitted to the configured sink, local spool, relay, or central transport boundary.
- A record should count as failed when the output layer definitively rejects it or exhausts retry/dead-letter policy for the current boundary.
- If the buffer accepts a record but delivery is still unresolved, the agent should avoid falsely counting it as centrally stored or successfully delivered to the server.
- Buffer health metrics should be linked to forwarding observations so the server can separate collection/filter issues from output/backpressure issues.

## Explicitly out of scope for the agent

The agent must not own these framework functions:

- historical utilization state,
- rule metadata or detection dependency evaluation,
- usage-purpose classification,
- central storage or retention-window evaluation,
- utilization metrics,
- gap classification,
- deterministic suggestions,
- cost optimization decisions.

The server computes those outputs from agent observations, server ingestion/storage facts, ruleset metadata, and policy configuration.

## Implementation order for this repository

1. Add shared observation models for `LogKey`, common metadata, pipeline counts, source health, and filter summary.
2. Add an in-process observation accumulator keyed by profile/source/channel/provider/event ID/window.
3. Instrument Windows Event Log input to increment `readCount` by `LogKey`.
4. Instrument the KQL/profile filtering stage to increment `keptAfterFilterCount` and `discardedCount`.
5. Instrument sinks or the buffer host to increment `forwardedCount` and `forwardFailedCount`.
6. Emit observations as normal structured output records so existing NDJSON/buffer paths can carry them.
7. Add fixture tests that prove emitted observation JSON matches the planned record kinds and field names.

