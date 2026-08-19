# Doom Forward reliability demonstrator

## Purpose

This capability is a **DeltaZulu.Forward transport demonstrator**, not a Doom product feature. It sends a compact but visually obvious binary frame stream through real `ForwardSession` handshakes, session windows, acknowledgements, reconnections, and `RawEnvelope` handlers. The terminal display makes received frames observable; the metrics make the transport behavior measurable.

The stream remains deliberately separate from normal security telemetry. Resource events use `TypedBatch`/`ForwardLogBatch` and the agent’s existing event pipeline. Demonstration frames use `RawEnvelope`, the `doom-frame-v1` catalog, and a bounded in-memory display sink. [1]

> A successful Doom display acknowledgement means that the collector validated and copied a frame into transient latest-frame memory. It does **not** prove durable recording, browser rendering, or lossless replay. It demonstrates a bounded live-delivery contract under the established Forward session mechanisms.

## Build and run

The project can normally restore `DeltaZulu.Forward` through the repository package configuration. For development against the companion working copy, provide its project path as shown below.

```bash
dotnet build src/DeltaZulu.Agent.DoomDisplay/DeltaZulu.Agent.DoomDisplay.csproj \
  -p:DeltaZuluForwardProject=/absolute/path/to/DeltaZulu.Forward/src/DeltaZulu.Forward/DeltaZulu.Forward.csproj
```

Start a collector on a loopback-only port.

```bash
dotnet run --project src/DeltaZulu.Agent.DoomDisplay -- collector \
  --port 46000 --metrics-json collector-metrics.json
```

Run a bounded 24-frame benchmark at 30 FPS. It intentionally closes and reopens the Forward session after each eight-frame segment, preserving the frame sequence across the reconnects.

```bash
dotnet run --project src/DeltaZulu.Agent.DoomDisplay -- forwarder \
  --host 127.0.0.1 --port 46000 --width 160 --height 100 --fps 30 \
  --frames 24 --max-in-flight 4 --disconnect-every 8 \
  --metrics-json forwarder-metrics.json
```

## What is exercised

| Demonstrator feature | Forward mechanism exercised | Evidence |
|---|---|---|
| Connection establishment | TCP connection plus Forward offer/ack handshake | Sender connection log and successful acknowledged frames. |
| Bounded sender pressure | `RequestedWindowSize` and session credit acquisition | `CurrentInFlight` and `MaximumInFlight` in sender JSON. |
| Application acknowledgements | `SendRawEnvelopeAsync` waits for committed acknowledgment | `AcknowledgedFrames`, mean acknowledgment latency, and maximum latency. |
| Deliberate recovery | Sender disposes a completed session and opens a new one | `Reconnects` and continuous sequence numbers. |
| Collector validation | `DoomInputDecoder` validates MessagePack and BGR24 contract | `CollectorRejectedFrames`, with invalid data receiving non-success outcome. |
| Bounded presentation | Two reusable frame slots with newest-frame replacement | `ReplacedPendingFrames`, rendered rate, and sequence progression. |
| End-to-end freshness | Capture timestamp is retained through the buffer | `LastCaptureAgeMilliseconds` in collector metrics and terminal status. |

## Metrics

| Metric | Interpretation |
|---|---|
| `SendAttempts` | Number of send operations started by the forwarder. |
| `AcknowledgedFrames` | Number of frame sends for which the collector returned a successful session outcome. |
| `FailedSends` | Send operations that faulted before a successful acknowledgment. |
| `Reconnects` | New Forward sessions established after the first one. Controlled disruption produces this intentionally. |
| `MeanAcknowledgmentLatencyMilliseconds` | Mean wall-clock time from sender entering `SendRawEnvelopeAsync` until successful completion. It includes transport, session, collector decode, and in-memory copy time. |
| `MaximumAcknowledgmentLatencyMilliseconds` | Highest observed acknowledgment latency. Use it to identify spikes, not as a durable-delivery measure. |
| `CurrentInFlight` / `MaximumInFlight` | Demonstrator-level sends currently awaiting outcome and their observed maximum. This remains bounded by `--max-in-flight`; the Forward credit window may impose a tighter bound. |
| `CollectorAcceptedFrames` | Frames decoded and copied to the collector back slot. |
| `CollectorRejectedFrames` | Frames rejected for wrong frame type, malformed MessagePack, or semantic contract failure. |
| `SequenceGaps` | Missing source sequence values observed by the collector. A nonzero value demonstrates a frame did not reach the collector; it is expected only when a failure was intentionally induced or a live stream was configured to drop stale frames upstream. |
| `OutOfOrderFrames` | Source sequence values not strictly greater than the last accepted sequence. A nonzero value indicates duplicates or out-of-order delivery at this application boundary. |
| `ReplacedPendingFrames` | Latest-frame-wins display discards. This is acceptable for a live viewer and indicates renderer pressure, not a Forward delivery failure. |
| `LastCaptureAgeMilliseconds` | Approximate age from source capture to the current collector snapshot. |

## Controlled disruption versus fault injection

`--disconnect-every N` is a **controlled session turnover**. The forwarder finishes its pending sends, disposes the session, reconnects, and continues with the next sequence number. It verifies repeated handshake and session establishment without manufacturing unacknowledged loss.

It does not replace hostile-network or crash testing. An abrupt collector kill, cable removal, or invalid-frame injection tests different behavior. Those tests should be run only in an isolated environment because the live sender treats unacknowledged failed sends as failed application operations; it does not persist a video backlog or replay stale frames after reconnect.

## Interpretation and acceptance criteria

For the bounded command above, the expected forwarder report has `SendAttempts = 24`, `AcknowledgedFrames = 24`, `FailedSends = 0`, `Reconnects = 2`, and an in-flight maximum no greater than the `--max-in-flight` value. The collector report should have `CollectorAcceptedFrames = 24`, `CollectorRejectedFrames = 0`, `SequenceGaps = 0`, and `OutOfOrderFrames = 0`.

The collector may report `ReplacedPendingFrames > 0` and fewer `RenderedFrames` than received frames. That is the intended latest-frame-wins policy: it proves the renderer cannot cause an unbounded queue. Treat persistent drops at a desired viewing rate as a capacity signal to lower the source FPS or increase presentation capacity.

## Security boundary

The collector binds to loopback for the demonstration. Do not expose it on a network without the TLS and peer-authentication model used by the established Forward deployment. Forward’s binary framing and CRC are not a substitute for a protected transport channel. [1]

## References

[1]: FORWARD_PROTOCOL_SPECIFICATION.md "DeltaZulu.Forward Protocol Specification"
