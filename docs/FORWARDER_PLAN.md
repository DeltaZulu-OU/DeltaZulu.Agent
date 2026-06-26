# Forwarder-first implementation plan

This plan revises the SIEM-agent roadmap now that `DeltaZulu.Buffer` exists as a working durable-buffer project and `RELP.Net` is tracked as the intended RELP transport submodule. The goal is to add forwarder behavior first, prove the delivery loop with a separate demo collector that prints received RELP batches to the console, and only then perform broader project restructuring.

## Current architectural reading

The repository already has the important seams for a Clean/Onion architecture:

- `DeltaZulu.Agent.Core` owns collector-facing abstractions such as source events, profile execution, resource outputs, inputs, and sinks.
- Input projects stay resource-specific: syslog, files, auditd, and Windows inputs.
- `DeltaZulu.Agent.Kql` is already an adapter around the KQL runtime rather than something every input must know about.
- `DeltaZulu.Agent.Outputs.Ndjson` owns serialization-oriented sinks.
- `DeltaZulu.Buffer` is a durable, disk-backed buffer that can sit between filtered records and the network forwarder.
- `external/RELP.Net` is the intended RELP foundation, but its implementation details must remain outside the domain/application model.

That means the next step should not be a large namespace or solution reshuffle. Build the forwarder path against the current seams, then extract or rename projects only where the implementation proves a boundary is real.

## Revised target flow

```text
Input adapter
  -> SourceEvent
  -> KQL profile filtering
  -> ResourceOutputRecord
  -> Delivery envelope bytes
  -> DeltaZulu.Buffer durable chunk
  -> Forwarder sender
  -> RELP.Net transport adapter
  -> ACK / retry / dead-letter decision
```

For local validation, keep the agent in the forwarder/client role and run a separate demo collector process that speaks RELP:

```text
ResourceOutputRecord
  -> NDJSON or delivery-envelope bytes
  -> DeltaZulu.Buffer
  -> RELP.Net forwarder transport
  -> separate demo RELP collector
  -> console print + RELP rsp 200
```

This proves buffer-to-forwarder ownership, batching, retry behavior, and ACK-to-delete semantics without making the agent host act as a collector/server.

## Clean/Onion boundaries with minimal restructuring

| Ring / layer | Keep or add | Responsibility | Dependency rule |
| --- | --- | --- | --- |
| Domain | Prefer `DeltaZulu.Agent.Core` for now; extract `DeltaZulu.Agent.Domain` later only if Core becomes too mixed. | Plain records and policies: source event, resource output, delivery identity, batch identity, acknowledgements, checkpoint intent, health snapshots. | No RELP.Net, `SslStream`, files, KQL runtime, Zstd, or Rx-specific transport types. |
| Application | Add a small delivery namespace/project only when needed. | Use cases and ports: enqueue filtered output, dispatch batches, commit ACKs, count failures, report health. | Depends on domain contracts and interfaces, not concrete infrastructure. |
| Infrastructure.Inputs | Existing input projects. | Collect and parse events into resource-native fields. | No buffer or transport ownership. |
| Infrastructure.Filtering | Existing `DeltaZulu.Agent.Kql`. | Execute resource profiles. | Hidden behind profile execution contracts. |
| Infrastructure.Buffer | Existing `DeltaZulu.Buffer`. | Crash-safe chunking, retry, backpressure, dead-lettering, recovery. | Payload-format agnostic; does not know RELP semantics. |
| Infrastructure.Transport | Forwarder/transport adapter. | RELP.Net over TCP/TLS, ACK interpretation, reconnect/backoff/failover policy. | May depend on RELP.Net; must not leak RELP types inward. |
| Infrastructure.Serialization | Existing NDJSON output can provide the first payload format. | Convert output records or delivery envelopes to bytes. | Serialization details stay outside domain policies. |
| Host | Existing agent CLI plus separate demo collector executable. | Agent CLI wires inputs, profiles, buffer, and RELP sender; demo collector receives RELP frames for local validation only. | Composition root only. |

## Forwarder-first backlog

### P0: Forwarder spike with separate demo collector

1. Add a forwarder path that can:
   - read existing CLI-produced `ResourceOutputRecord` values,
   - serialize them to the first delivery payload format,
   - write them into `DeltaZuluBufferHost<T>`, and
   - dispatch chunks through an `IChunkSender` implementation.
2. Add a separate demo collector executable or test fixture that:
   - listens locally,
   - accepts RELP syslog frames,
   - prints received records or batches to the console, and
   - returns explicit RELP `rsp 200` acknowledgements.
3. Keep the demo collector out of the agent CLI so the agent remains a forwarder/client.
4. Add tests for successful send, transient send failure, retry, permanent failure/dead-letter, and restart recovery where feasible.

### P0: Delivery contract and identity

Define minimal contracts before integrating RELP:

```csharp
public sealed record DeliveryRecord(
    string AgentId,
    string SourceId,
    string ProfileId,
    string RecordId,
    DateTimeOffset CreatedAt,
    ReadOnlyMemory<byte> Payload);

public sealed record DeliveryBatch(
    string BatchId,
    IReadOnlyList<DeliveryRecord> Records);

public sealed record DeliveryAck(
    string BatchId,
    bool Accepted,
    string? Reason);

public interface IForwarderTransport
{
    ValueTask<DeliveryAck> SendAsync(DeliveryBatch batch, CancellationToken cancellationToken);
}
```

These contracts may live in `DeltaZulu.Agent.Core` or a small application project at first. Keep them RELP-neutral so test fixtures and the RELP transport share the same application boundary.

### P1: RELP.Net adapter hardening

Continue hardening the RELP.Net-backed transport adapter:

- Map each `DeliveryBatch` to one or more RELP frames.
- Treat RELP `rsp 200` as transport-level success for the corresponding batch or frame.
- Keep durable commit/delete behavior in the buffer/application side, not in the RELP adapter.
- Add reconnect with jittered backoff, endpoint selection, and clear transient/permanent failure classification.
- Add certificate validation, endpoint failover, and operational receiver documentation after plain RELP/TLS wiring is validated.

Do not use any bounded logging-provider queue semantics from RELP.Net as the SIEM delivery model. `DeltaZulu.Buffer` is the authoritative durability and backpressure layer.

### P1: Metadata and checkpoint correctness

The forwarder path should preserve agent/source/profile metadata outside of user-controlled KQL projections. A profile should not be able to accidentally remove collector identity or delivery metadata by omitting `_metadata` from `project`.

Checkpoint advancement should eventually be tied to durable enqueue, not network ACK:

- If reading succeeds but durable enqueue fails, the source checkpoint must not advance.
- If durable enqueue succeeds and send fails, the event can be resent from the buffer.
- Therefore the practical delivery model is at-least-once with stable IDs and server-side deduplication.

### P2: Restructure only after boundaries harden

Once the forwarder path and RELP adapter pass tests, consider low-risk restructuring:

1. Introduce `DeltaZulu.Agent.Application` if delivery orchestration grows beyond simple host wiring.
2. Introduce `DeltaZulu.Agent.Transport.Relp` for the RELP.Net adapter.
3. Optionally split `DeltaZulu.Agent.Domain` from Core if Core accumulates infrastructure references.
4. Rename or split `Outputs.Ndjson` only if it is used as a delivery serializer rather than a terminal output sink.
5. Keep existing input projects stable; do not move input adapters just to satisfy a diagram.

## Revised priorities

| Priority | Item | Reason |
| --- | --- | --- |
| P0 | Build RELP forwarder client and separate demo collector. | Validates the end-to-end buffer-forwarder loop without making the agent host act as a server. |
| P0 | Define RELP-neutral delivery records, batches, ACKs, and transport port. | Prevents RELP.Net details from leaking into the agent core. |
| P0 | Wire `DeltaZulu.Buffer` after KQL filtering and before transport. | Uses the working buffer where SIEM durability belongs. |
| P0 | Add observability counters for written, sent, acknowledged, retried, dead-lettered, and oldest buffered age. | Makes failures visible during demo and production hardening. |
| P1 | Harden the RELP.Net adapter over the same transport port. | Keeps production transport behind the existing application boundary without changing input/filtering code. |
| P1 | Add TLS/certificate validation and expiry reporting. | Makes RELP production-credible after the plain transport works. |
| P1 | Add delivery IDs and at-least-once deduplication fields. | Supports safe resend after crash or network failure. |
| P2 | Add checkpoint contracts per source family. | Prevents loss/duplicates during source restarts. |
| P2 | Perform project splits/renames only where code pressure justifies them. | Preserves momentum and avoids architecture-only churn. |
| P2 | Add service/daemon hosts, config reload, and signed config. | Operational hardening after delivery semantics are proven. |

## Decision summary

Build the forwarder first. Use `DeltaZulu.Buffer` as the durable queue, use a separate console-printing demo collector as the local validation target, and keep RELP.Net behind a transport adapter. This respects Clean/Onion by depending inward on delivery contracts while avoiding a disruptive restructuring before the new boundaries have been proven in code.
