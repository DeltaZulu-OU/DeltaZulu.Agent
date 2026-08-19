# Doom Display Server Proof of Concept

## Purpose and scope

This proof of concept turns the existing **DeltaZulu.Forward** session transport into a compact, real-time pixel tunnel. It is intentionally isolated from the resource-event pipeline: normal agent records remain `TypedBatch` messages containing `ForwardLogBatch` values, while display frames use the protocol’s `RawEnvelope` frame type. This preserves the field catalog and SIEM delivery semantics for security telemetry.

> **Protocol correction:** the transport is inspired by RELP but is not literal RELP or compatible with `librelp`. It uses a fixed binary header, typed handshake, acknowledgment window, and `RawEnvelope` support. The relevant on-wire contract is DeltaZulu.Forward, not the RELP text grammar. [1]

The executable includes a procedural Doom-style scene solely to exercise the stream. It does **not** include Doom source code, WAD assets, or a game engine. A licensed .NET or native source-port adapter should implement `IDoomFrameSource` and return the same BGR24 buffer to substitute a real render loop.

| Component | Responsibility | POC implementation |
|---|---|---|
| Frame source | Produce an uncompressed BGR24 frame | `SyntheticDoomFrameSource`; replaceable through `IDoomFrameSource` |
| Frame contract | Version, geometry, format, sequence, and pixel bytes | `DoomFramePacket`, encoded with MessagePack |
| Forwarder | Send a frame and await a session acknowledgment | `ForwardSession.SendRawEnvelopeAsync` |
| Doom input decoder | Convert a RawEnvelope payload into a validated frame or controlled decode rejection | `DoomInputDecoder` |
| Doom output sink | Copy frames into the back slot, retain only the newest pending frame, and own the render loop | `DoomOutputSink` plus `LatestFrameDoubleBuffer` |
| Renderer | Present the front-slot frame without a GUI dependency | ANSI true-colour half-block renderer with receive/render/discard counters |

## MessagePack payload contract

`DoomFramePacket` uses MessagePack indexed keys. Indexed keys make the payload compact and are the MessagePack-CSharp project’s recommended choice for performance and size. [2] The inner MessagePack document becomes the opaque payload of a DeltaZulu.Forward `RawEnvelope`; the Forward library then adds its own batch identifier, binary frame header, CRC, transaction number, handshake, flow control, and acknowledgment machinery. [1]

| Key | Field | Type | Constraint |
|---:|---|---|---|
| 0 | `contractVersion` | unsigned 16-bit integer | Exactly `1` |
| 1 | `sequence` | signed 64-bit integer | Monotonically increasing at the source |
| 2 | `width` | signed 32-bit integer | 1 through 320 |
| 3 | `height` | signed 32-bit integer | 1 through 200 |
| 4 | `pixelFormat` | byte enum | `Bgr24` only (`1`) |
| 5 | `pixels` | MessagePack binary | Exactly `width × height × 3` bytes |

A 160×100 BGR24 frame contains 48,000 raw pixel bytes; a 320×200 frame contains 192,000 bytes. The codec rejects empty payloads, unsupported format or contract versions, out-of-range dimensions, and incorrect byte counts before rendering. The collector also constrains the outer transport frame to the maximum legal frame plus a small MessagePack/transport allowance.

## Running the demonstration

Build the solution with the .NET 10 SDK, then start the collector in one terminal and the forwarder in another. The collector deliberately binds only to the loopback interface. The default 160×100 at 15 FPS configuration is suitable for a terminal proof of concept; terminal rendering, not serialization, becomes the practical rate limit.

```bash
dotnet run --project src/DeltaZulu.Agent.DoomDisplay -- collector --port 46000
```

```bash
dotnet run --project src/DeltaZulu.Agent.DoomDisplay -- forwarder \
  --host 127.0.0.1 --port 46000 --width 160 --height 100 --fps 15
```

Press `Ctrl+C` in either terminal to stop it. On terminals without ANSI true-colour support, the display can be malformed; a production Windows renderer should instead copy the same validated BGR24 buffer to a `WriteableBitmap` on its UI thread.

## Reliability demonstrator

The proof of concept now supports a bounded forwarder benchmark, acknowledgement-latency reporting, collector continuity checks, controlled session turnover, and JSON metrics reports. Run commands and interpretation criteria are documented in [DOOM_FORWARD_RELIABILITY_DEMONSTRATOR.md](DOOM_FORWARD_RELIABILITY_DEMONSTRATOR.md).

## Reliability and security boundaries

The session’s four-frame window applies explicit backpressure: a forwarder awaits the collector acknowledgment before the window can advance. `DoomInputDecoder` first performs the MessagePack and semantic validation; `DoomOutputSink` then copies the completed pixels into its back slot and the collector acknowledges that in-memory hand-off. The render loop alone swaps the back slot to the front slot. When a newer frame arrives before that swap, it overwrites the pending back-slot contents and increments the discard counter. This latest-frame-wins policy bounds display latency and prevents an unbounded frame queue, but it deliberately does not retain every frame.

The acknowledgment therefore means **validated and copied into transient display memory**, not rendered or durably stored. This is appropriate for a live display but is not durable video recording or lossless replay. A collector intended to retain every frame must write each accepted payload to a bounded durable spool before returning a committed acknowledgment.

The collector binds to `127.0.0.1` and should remain local. If the listener is intentionally exposed beyond the host, place the Forward TCP stream inside mutually authenticated TLS; the protocol documentation specifies that TLS is external to its own handshake and that CRC-32 detects accidental corruption rather than providing tamper resistance. [1]

## Integration guidance

A source-port adapter should hold a reusable BGR24 buffer, copy or expose a completed frame only at a render-frame boundary, and construct a packet with the source frame number. It should not route frames through `ForwardLogBatch`, JSON, the resource-event durable buffer, or the Windows Event Log. Those are typed security-event paths with different schemas, retention requirements, and operational priorities.

A production GUI collector can replace only the renderer supplied to `DoomOutputSink`; the `DoomInputDecoder`, `DoomFramePacket`, buffer policy, and Forward session boundary remain unchanged. A WPF implementation should marshal the sink’s front-slot frame to the dispatcher and use one reusable BGR24 `WriteableBitmap`. The sink’s status line exposes receive FPS, rendered FPS, discarded pending-frame count, and last presented sequence, making pressure observable without retaining a backlog.

## References

[1]: FORWARD_PROTOCOL_SPECIFICATION.md "DeltaZulu.Forward Protocol Specification".
[2]: https://github.com/MessagePack-CSharp/MessagePack-CSharp "MessagePack-CSharp: Object serialization and indexed-key guidance".
