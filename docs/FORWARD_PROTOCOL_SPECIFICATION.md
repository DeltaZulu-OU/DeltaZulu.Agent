# DeltaZulu.Forward Protocol Specification (FWD-CONTRACT-v1)

## 0. Status

This is the authoritative, standalone wire-protocol specification for
**DeltaZulu.Forward**, DeltaZulu's proprietary agent-to-collector log
forwarding protocol. It exists because the protocol had accumulated a
correct but scattered description — spread across
[ADR 0011](adr/0011-deltazulu-forward-transport.md) (transport naming and
design), [ADR 0014](adr/0014-messagepack-wire-supersedes-avro.md) (wire
payload format), and the `DeltaZulu.Forward` library's own source comments
and README — without one document a reader or a from-scratch reimplementer
could point to. The library's own source already calls its wire contract
`FWD-CONTRACT-v1` in several places (`ForwardObjectFormatter`,
`ForwardLogBatchCodec`); this document is that contract, written down.

This specification is derived by direct inspection of the reference
implementation (`DeltaZulu.Forward`, .NET) as of the version this
repository pins in `Directory.Packages.props`. Where the reference
implementation and this document conflict, treat the conflict as a bug in
one of the two and fix it — this is not a specification written ahead of,
and independent from, an implementation; it is a specification extracted
from one, in the style of an RFC written after running code exists.

**Format identifier:** `FWD-CONTRACT-v1`
**Protocol wire version:** `1` (`ForwardFrameHeader.CurrentProtocolVersion`)
**Reference implementation:** [`DeltaZulu.Forward`](https://github.com/DeltaZulu-OU/DeltaZulu.Forward) (.NET)
**Consuming implementation:** this repository, via `ForwarderInput` (collector-role
adapter) and `ForwarderTransport` (forwarder-role adapter) in
`src/DeltaZulu.Pipeline/{Inputs,Outputs}/Forwarder/`

## 1. Introduction

DeltaZulu.Forward moves already-typed (or explicitly raw) log batches from
an agent to a collector reliably: at-least-once delivery, application-level
acknowledgement bound to durable commit (not merely TCP delivery),
multiple batches in flight at once under a negotiated credit window, and
collector-side deduplication so at-least-once redelivery never
double-processes a batch.

The name follows the [fluentd Forward
protocol](https://chronosphere.io/learn/forward-protocol-fluentd-fluent-bit/)
convention — a product-scoped name for a product-scoped protocol — but the
two protocols are unrelated on the wire. DeltaZulu.Forward's actual design
lineage is **RELP** (Reliable Event Logging Protocol), from which it
harvests the core insight (acknowledgement bound to durable commit, not
delivery) and several structural ideas, while replacing RELP's text
framing with a binary one. See §12 for the full comparison against both
RELP and fluentd Forward.

### 1.1 Design goals

- **Ack means committed, not delivered.** An acknowledgement is a
  statement that the collector has durably accepted a batch, not that TCP
  moved the bytes.
- **Binary-safe, octet-counted framing.** No text command verbs, no
  delimiter scanning, no payload assumptions (unlike RELP's `rsp`/`syslog`
  frames or fluentd's MessagePack-RPC event stream).
- **Windowed, not single-flight.** Multiple batches may be outstanding at
  once, up to a credit window negotiated at handshake and adjustable
  mid-session.
- **At-least-once with collector-side dedup.** Redelivery after a lost
  acknowledgement is expected and safe: the collector deduplicates by
  batch UUID before a batch reaches decode.
- **No degraded fallback wire format.** There is deliberately no
  "plain-text fallback for outages" mode. A fallback is a second contract
  every consumer would have to implement forever, and it reintroduces the
  type-reconstruction divergence this protocol exists to avoid. Outages
  are handled by spooling and replay at the caller's transport-adapter
  layer (see ADR 0011, ADR 0014), not by degrading the wire format.
- **Interop with the rsyslog/fluentd worlds is a non-goal on this
  channel.** Raw ingestion from such peers, if ever needed, is a separate
  input adapter feeding a parser — not this transport speaking their wire
  format.

### 1.2 Roles

A DeltaZulu.Forward session connects exactly two roles:

- **Forwarder** — dials out, sends the handshake offer (`Hello`), and
  originates batches (`TypedBatch` / `RawEnvelope`). In this repository,
  `ForwarderTransport` plays this role.
- **Collector** — accepts the connection, replies to the handshake
  (`HelloAck`), and acknowledges batches (`Ack`). In this repository,
  `ForwarderInput` plays this role.

Either role may originate `SchemaRequest`, `SchemaResponse`,
`DeadLetterForward`, `Control`, and `Close` frames; the protocol is not
strictly half-duplex once a session is open (see §7).

## 2. Terminology

| Term | Meaning |
| --- | --- |
| **Session** | One handshake-negotiated, stateful conversation over one TCP (optionally TLS) connection. |
| **Frame** | One length-prefixed, CRC-32-checked unit on the wire: a fixed 16-byte header plus a payload. |
| **Transaction number (`txnr`)** | A `uint32` in every frame header identifying the transaction the frame belongs to. Never zero. |
| **Batch** | One unit of durably-committable data, identified by a `Guid` (`BatchId`), carried as the payload of exactly one `TypedBatch` or `RawEnvelope` frame. Never split across frames. |
| **Credit window** | The number of batches a forwarder may have outstanding (sent, not yet acknowledged) at once. |
| **Dedup window** | A bounded, session-spanning set of recently seen batch UUIDs the collector uses to reject duplicate batches before decode. |
| **Schema fingerprint** | A `uint64` identifying a schema the peer may or may not already hold the bytes for. |

## 3. Transport

DeltaZulu.Forward runs over TCP. TLS, when used, is layered as a plain
stream transport **beneath** the framing — it is not woven into the
protocol's own handshake the way `librelp`'s TLS layering is. A
`ForwardConnection` wraps either a plain or an `SslStream`-wrapped
`TcpClient`; the framing and session logic above it are transport-agnostic
to that distinction.

There is no protocol-level requirement on which side initiates the TCP
connection versus which side is logically the forwarder or collector in
future deployments (for example, a collector could dial out to a
constrained-network agent), but the reference implementation and this
repository's usage always have the forwarder dial the collector.

## 4. Wire encoding conventions

Two independent encodings coexist in this protocol, and knowing which
applies where matters for a correct implementation:

1. **Frame headers and most frame payloads** use a hand-rolled,
   length-prefixed binary encoding (`ForwardPayloadWriter`/
   `ForwardPayloadReader` in the reference implementation). All multi-byte
   integers are **big-endian**.
2. **The `TypedBatch` frame's inner payload** (the `ForwardLogBatch`, see
   §10) is **MessagePack**, not the hand-rolled encoding. The
   `RawEnvelope` frame's inner payload is opaque to this protocol layer
   entirely (see §11).

Primitives used in encoding (1):

| Primitive | Wire form |
| --- | --- |
| `byte` | 1 byte |
| `bool` | 1 byte: `0x00` = false, any other value MUST be treated as `0x01`/true by a lenient reader, but a conforming writer always emits exactly `0x00` or `0x01`. |
| `uint16` | 2 bytes, big-endian |
| `uint32` | 4 bytes, big-endian |
| `uint64` | 8 bytes, big-endian |
| `Guid` | 16 raw bytes, **not** RFC 4122 wire order — see the callout below |
| `string` | `uint16` byte-length prefix, followed by that many UTF-8 bytes. Maximum 65,535 encoded bytes; a writer MUST reject a longer string rather than truncate it. |
| length-prefixed bytes | `uint32` byte-length prefix, followed by that many raw bytes |
| "remaining bytes" | Consumes every byte left in the payload; only valid as the last field of a payload |

> **Interop hazard — GUID byte order.** The reference implementation
> encodes a `Guid` with .NET's `Guid.ToByteArray()`, which is **not**
> RFC 4122 big-endian order: the first 4 bytes (`Data1`) and next 2 pairs
> of bytes (`Data2`, `Data3`) are little-endian on the wire, and only the
> trailing 8 bytes (`Data4`) are in RFC 4122 order. A non-.NET
> implementation that serializes a UUID in standard RFC 4122 byte order
> will produce **different bytes** for the same logical GUID string and
> will fail to interoperate. Any reimplementation MUST replicate .NET's
> specific mixed-endian `Guid` layout, not a standards-compliant UUID
> encoding, to be wire-compatible with `FWD-CONTRACT-v1`.

## 5. Frame format

Every frame is a fixed 16-byte header immediately followed by
`PayloadLength` bytes of payload (payload length 0 is valid — several
frame types, such as `Close`/`CloseAck`, carry no payload).

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
| FrameType (1)| Flags (1)   |       ProtocolVersion (2)       |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                    TransactionNumber (4)                     |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                       PayloadLength (4)                      |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                      PayloadChecksum (4)                     |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                   Payload (PayloadLength bytes)              |
:                              ...                              :
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

| Field | Size | Meaning |
| --- | --- | --- |
| `FrameType` | 1 byte | See §6. |
| `Flags` | 1 byte | Bitfield. Bit 0 (`0x01`) = `Compressed`. All other bits reserved and MUST be zero on write; a reader MUST ignore unrecognized bits rather than reject the frame. |
| `ProtocolVersion` | `uint16` | The wire protocol version the frame was written with. Currently always `1`. |
| `TransactionNumber` | `uint32` | See §8.1. MUST be `>= 1`; `0` is invalid and a conforming reader MUST reject a frame carrying it. |
| `PayloadLength` | `uint32` | Byte length of `Payload`. |
| `PayloadChecksum` | `uint32` | CRC-32 (ISO 3309 / IEEE 802.3 polynomial, i.e. the same CRC-32 used by zlib/gzip) of `Payload`. `0` when `PayloadLength` is `0`. |
| `Payload` | `PayloadLength` bytes | Frame-type-specific; see §6. |

A reader MUST reject a frame whose declared `PayloadLength`, combined with
the fixed 16-byte header, exceeds a configured maximum frame length
(default 1 MiB, i.e. `1024 * 1024` bytes header-plus-payload,
`ForwardParserOptions.DefaultMaxFrameLength`), and MUST reject a frame
whose payload fails its `PayloadChecksum` verification. Both are treated
as fatal for the connection, not per-frame recoverable errors — the CRC-32
check is corruption detection, not an application-level negative
acknowledgement path.

### 5.1 Compression

The `Compressed` flag bit and the handshake's negotiated compression
algorithm (§6.1/§6.2, `ForwardCompression`: `None = 0`, `Zstd = 1`) exist in the
wire format, but **the reference implementation's high-level send API does
not perform compression itself.** `ForwardSession.SendTypedBatchAsync` /
`SendRawEnvelopeAsync` always write frames with `Flags = None`. A caller
that wants compressed payloads must compress the bytes itself before
calling `Send*Async` (and decompress them itself on receipt, inside its
`BatchHandler`), and must use the lower-level `ForwardFrameTx` constructor
directly if it wants the `Compressed` flag bit actually set on the wire.
The core library takes no compile-time dependency on any compression
library; the reference examples use `ZstdSharp.Port` entirely outside the
core package. Treat compression as a payload-content concern the two
peers agree on out of band (informed by, but not enforced by, the
handshake's `CompressionOffered`/`CompressionSelected` fields), not as a
protocol-enforced framing feature in this version of the contract.

## 6. Frame types

| Value | Name | Sent by | Payload | Purpose |
| --- | --- | --- | --- | --- |
| `0` | `Hello` | forwarder | `ForwardHandshakeOffer` | Open a session: offer protocol version, catalog version, known schema fingerprints, compression, window sizes, resume token. |
| `1` | `HelloAck` | collector | `ForwardHandshakeAck` | Accept or reject the offer; grant windows; report unknown schema fingerprints. |
| `2` | `TypedBatch` | either | `ForwardBatchEnvelope` wrapping a MessagePack `ForwardLogBatch` | One catalog-typed batch, identified by UUID (§10). |
| `3` | `RawEnvelope` | either | `ForwardBatchEnvelope` wrapping opaque bytes | One batch whose payload format is defined by the input adapter at the collector tier, not by this protocol (§11). |
| `4` | `SchemaRequest` | either | `ForwardSchemaRequest` | Ask the peer for the schema bytes behind a fingerprint. |
| `5` | `SchemaResponse` | either | `ForwardSchemaResponse` | Answer a `SchemaRequest`, or proactively push a schema the peer flagged as unknown at handshake (§9). |
| `6` | `DeadLetterForward` | either | `ForwardDeadLetter` | Forward a batch that failed parsing/validation, with its original bytes and a reason. |
| `7` | `Ack` | either (reply to a batch frame, by whichever side received it) | `ForwardAckOutcome` | Acknowledge the batch named by the frame header's `TransactionNumber`. |
| `8` | `Control` | either | `ForwardControlMessage` | Backpressure: window adjustment or throttle. |
| `9` | `Close` | either | none | Request orderly session shutdown. |
| `10` | `CloseAck` | either (reply to `Close`) | none | Acknowledge orderly shutdown. |

Any other value is a protocol violation; a conforming session MUST treat
it as fatal for the connection (the reference implementation faults every
pending transaction and terminates the receive loop).

`Hello`/`HelloAck` are valid **only** during the initial handshake
exchange, which is synchronous and inline (see §7) — they are read
directly from the connection, not through the steady-state receive loop.
Receiving `Hello` or `HelloAck` at any other point in a session is a
protocol violation.

### 6.1 `Hello` payload — `ForwardHandshakeOffer`

```
ProtocolVersion         uint16
SessionResumeToken      Guid (16 bytes)
CatalogVersion          string
RequestedWindowSize     uint32
DedupWindowSize         uint32
CompressionOffered      byte (ForwardCompression)
KnownSchemaFingerprintCount  uint16
KnownSchemaFingerprints uint64[KnownSchemaFingerprintCount]
```

`SessionResumeToken` of `Guid.Empty` requests a new session rather than
resuming one; the resume mechanics beyond carrying this token are not
further specified by the current protocol version — no reference
implementation behavior currently branches on a non-empty token being
recognized versus not.

### 6.2 `HelloAck` payload — `ForwardHandshakeAck`

```
Accepted                     bool
ProtocolVersion               uint16
SessionId                     Guid (16 bytes)
GrantedWindowSize             uint32
DedupWindowSize                uint32
CompressionSelected            byte (ForwardCompression)
UnknownSchemaFingerprintCount  uint16
UnknownSchemaFingerprints      uint64[UnknownSchemaFingerprintCount]
RejectReason                   string
```

If `Accepted` is `false`, the offerer MUST treat the handshake as
rejected and MUST NOT proceed to steady-state; `RejectReason` is a
human-readable explanation, not a machine-parsed code. `DedupWindowSize`
in the ack is the size the collector will actually enforce for inbound
deduplication (a collector MAY grant a different size than the offer's
`DedupWindowSize` requested; both sides independently size their own
dedup window to at least `max(1, DedupWindowSize)`).

### 6.3 `TypedBatch` / `RawEnvelope` payload — `ForwardBatchEnvelope`

```
BatchId    Guid (16 bytes)
Payload    remaining bytes
```

One batch per frame; a batch is never split across frames, and a frame
never carries more than one independently committable batch. For
`TypedBatch`, `Payload` is a MessagePack-encoded `ForwardLogBatch` (§10).
For `RawEnvelope`, `Payload` is opaque to this protocol (§11).

### 6.4 `SchemaRequest` payload — `ForwardSchemaRequest`

```
SchemaFingerprint    uint64
```

### 6.5 `SchemaResponse` payload — `ForwardSchemaResponse`

```
SchemaFingerprint    uint64
Found                bool
SchemaBytes          remaining bytes
```

`SchemaBytes` is empty when `Found` is `false`. The content and format of
`SchemaBytes` is not specified by this protocol version — it is
whatever the catalog/schema layer above this transport defines.

### 6.6 `DeadLetterForward` payload — `ForwardDeadLetter`

```
OriginalBatchId    Guid (16 bytes)
Reason             string
OriginalPayload    remaining bytes
```

### 6.7 `Ack` payload — `ForwardAckOutcome`

```
StatusCode    byte
Detail        string
```

`Detail` is the empty string when there is nothing to report (encoded and
decoded as `null` at the application layer when empty). **`StatusCode ==
0` is the only value this protocol version gives fixed meaning to: it
means the batch was durably committed.** Every other value (`1`-`255`) is
application/session-defined "not committed," distinguished only by the
human-readable `Detail` string — this version of the contract does not
define a registry of non-zero status codes. Implementations observed in
this repository use `1` for a generic rejection (with `Detail` carrying
the reason) and `2` for "no batch handler configured" / "unsupported
frame type," but a new implementation MUST NOT assume these are
protocol-guaranteed; MUST NOT branch behavior on any non-zero code beyond
"not committed"; and SHOULD treat `Detail` as advisory/diagnostic text
only.

### 6.8 `Control` payload — `ForwardControlMessage`

```
ControlType    byte (0 = WindowAdjust, 1 = Throttle)
Value          uint32
```

- `WindowAdjust`: `Value` is the new total credit-window capacity (§8.2).
  It replaces, rather than adjusts by delta, the previous capacity.
- `Throttle`: `Value > 0` means "pause sending"; `Value == 0` means
  "resume." This version of the contract does not attach duration
  semantics to a nonzero `Value` beyond "throttled until a `Value == 0`
  Throttle control frame arrives" — despite `ForwardFrameType.Throttle`'s
  doc comment describing it as "pause for a duration," the reference
  implementation's receive-side handling (`ForwardSession.HandleControl`)
  treats any nonzero value identically as an indefinite pause and does
  not interpret `Value` as a duration or timeout. Do not rely on `Value`
  encoding a specific pause length until a future contract version
  defines that encoding explicitly.

> **Implementation-status note.** As of the reference implementation
> version this specification describes, `ForwardSession` has no public
> API to *send* a `Control` frame — only to receive and react to one
> (`IsThrottled`, credit-window adjustment). A peer that wants to signal
> backpressure must construct and write the frame itself via the
> lower-level `ForwardConnection.WriteFrameAsync` /
> `ForwardFrameTx.FromPayload(ForwardFrameType.Control, ...)` API. This is
> a gap in the reference SDK's convenience surface, not a wire-format
> restriction — the frame type and payload are fully specified and any
> conforming peer may send one.

### 6.9 `Close` / `CloseAck` payload

Both frame types carry no payload (`PayloadLength == 0`).

## 7. Session lifecycle

### 7.1 Handshake

The handshake is synchronous and precedes the steady-state receive loop
entirely — both sides read/write it inline, not through the same
concurrent dispatch used for everything after.

```
Forwarder                                   Collector
    |                                            |
    |--- Hello (ForwardHandshakeOffer) --------->|
    |                                            | negotiate(offer) -> ack
    |<-- HelloAck (ForwardHandshakeAck) ---------|
    |                                            |
    | (if Accepted == false: abort, no further   |
    |  frames are valid on this connection)       |
    |                                            |
    | for each ack.UnknownSchemaFingerprints:     |
    |--- SchemaResponse (proactive push) ------->|  (§9)
    |                                            |
    |============ steady state (§7.2) ===========|
```

- The `Hello` frame's `TransactionNumber` is chosen by the forwarder from
  its own transaction-number sequence (§8.1); the collector's `HelloAck`
  **echoes that same transaction number** rather than drawing from its
  own sequence.
- `HelloAck.SessionId` is assigned by the collector (`Guid.NewGuid()` in
  the reference implementation) and becomes the session's identity for
  its lifetime.
- Both sides size their credit window to the *granted* value
  (`GrantedWindowSize`) and their dedup window to
  `max(1, DedupWindowSize)` from the ack, immediately after a successful
  handshake, before either side is considered "active."
- Only after handshake success does either side start its background
  receive pump; frames other than `Hello`/`HelloAck` sent before this
  point are not valid.

### 7.2 Steady state

Once active, both peers run a concurrent background receive loop that
dispatches inbound frames by type (batch frames, `Ack`, `Control`,
`SchemaRequest`/`SchemaResponse`, `DeadLetterForward`, `Close`/`CloseAck`)
while the application sends batches, schema requests, and dead letters
independently. Multiple batches may be outstanding at once, bounded by
the credit window (§8.2); each is resolved independently as its `Ack`
frame arrives, not in send order.

### 7.3 Sending a batch

```
Forwarder                                   Collector
    |                                            |
    | acquire one credit window slot              |
    | (wait if throttled or window exhausted)      |
    |                                            |
    |--- TypedBatch or RawEnvelope -------------->|
    |    (ForwardBatchEnvelope{BatchId, ...})      | admit to dedup window
    |                                            | (reject duplicate BatchId
    |                                            |  with a committed-style
    |                                            |  Ack, no reprocessing)
    |                                            | hand to BatchHandler
    |<-- Ack (ForwardAckOutcome) ------------------|
    |                                            |
    | release credit window slot                  |
```

- The `TransactionNumber` on the batch frame is drawn from the sender's
  own sequence; the `Ack` frame's `TransactionNumber` **echoes it**. This
  is how a sender with several batches in flight matches each `Ack` back
  to the batch it acknowledges — correlation is by transaction number,
  not by `BatchId` (a receiver could infer the mapping by remembering
  which `BatchId` it sent under which `TransactionNumber`, but the
  protocol's correlation mechanism itself is the transaction number).
- If the outcome's `StatusCode != 0` (not committed), the sender MUST
  treat the batch as not durably accepted. The reference implementation
  raises an application-level exception in this case; it does not
  automatically retry within `ForwardSession` — retry/redelivery is a
  caller concern (an at-least-once resend of the same `BatchId` is always
  safe because of collector-side dedup).
- A duplicate `BatchId` presented to an already-admitted dedup window is
  acknowledged as committed (`StatusCode == 0`, `Detail` noting it was a
  duplicate) **without** being handed to the batch handler again. This is
  the mechanism that makes at-least-once redelivery safe: the sender
  cannot distinguish "collector processed this the first time" from
  "collector recognized this as a resend" from the `Ack` alone, and does
  not need to.

### 7.4 Orderly shutdown

```
Initiator                                   Peer
    |                                            |
    |--- Close ----------------------------------->|
    |                                            | stop admitting new work
    |<-- CloseAck -----------------------------------|
    |                                            |
    | (both sides stop their receive pump)         |
```

Either side may initiate. The initiator's `CloseAsync` sends `Close` and
waits for the corresponding `CloseAck` (correlated by echoed transaction
number) before tearing down its receive pump; the responder replies with
`CloseAck` immediately upon receiving `Close` and then stops itself.
Sending a `Close`/receiving a `CloseAck` sequence is best-effort during
teardown — a caller closing after a failed send treats close errors as
non-fatal.

### 7.5 Abnormal termination

A transport-level failure (connection reset, malformed frame, checksum
failure, frame-size violation, unexpected frame type) is fatal to the
session: every transaction currently awaiting acknowledgement is faulted
(not silently dropped — the caller observes an exception), and the
receive pump exits. There is no automatic reconnection or session
resumption inside `ForwardSession` itself; a caller that wants
reconnect-with-backoff (as `ForwarderTransport` in this repository does)
implements it as a new `ForwardConnection` + new `ForwardSession` +
fresh handshake, entirely above this layer. `SessionResumeToken` exists
in the handshake vocabulary (§6.1) for a future resumption design but is
not currently acted on by the reference implementation beyond being
carried.

## 8. Reliability semantics

### 8.1 Transaction numbers

Each session side maintains its **own independent** monotonically
advancing `uint32` counter, starting at `1`, for transactions it
originates (`Hello`, `TypedBatch`/`RawEnvelope`, self-initiated
`SchemaRequest`, proactively-pushed `SchemaResponse` at handshake,
`DeadLetterForward`, `Close`). Reply frames (`HelloAck`, `Ack`,
reactive `SchemaResponse`, `CloseAck`) **echo** the transaction number of
the frame they reply to rather than drawing from the replier's own
sequence. `0` is reserved and invalid; the counter wraps from
`0xFFFFFFFF` back to `1` (never back to `0`) rather than overflowing.
Transaction numbers are scoped to one session — they carry no meaning
across a reconnect.

There is no session-wide single sequence: two frames with the same
`TransactionNumber` value, one sent by each side, are unrelated unless
one is specifically a reply to the other by the correlation rules above.

### 8.2 Credit window (flow control)

A forwarder must acquire one unit of credit before sending a batch frame
and releases it when that batch's outcome (success or failure) is known.
The window's capacity starts at the collector-granted value from the
handshake and can be changed mid-session by a `Control`
(`WindowAdjust`) frame from the collector, which sets an entirely new
capacity (not a delta). Growing the window immediately wakes any
forwarder blocked waiting for credit; shrinking it only reduces future
availability — it never revokes credit already granted for in-flight
batches.

### 8.3 Throttle (backpressure)

A `Control` (`Throttle`) frame with `Value > 0` tells the peer to pause
sending batches entirely, independent of remaining credit-window
capacity; `Value == 0` clears the pause. A throttled sender blocks new
batch sends until cleared — this is a stronger, orthogonal signal to the
credit window, not a special case of it.

### 8.4 Deduplication

The collector maintains a bounded, session-**spanning** (not
session-scoped) set of recently admitted batch UUIDs — it is not reset by
a reconnect, so a fresh session after a dropped connection still rejects
a batch the forwarder is resending because it never saw the earlier
session's `Ack`. Eviction is FIFO by admission order once the configured
capacity (`DedupWindowSize`, at least 1) is exceeded. Deduplication
happens **before** the batch is handed to the batch handler / decoded —
guaranteed duplicates from at-least-once delivery never reach application
logic, let alone a typed detection pipeline, as double-counted events.

### 8.5 What "at least once" does and does not guarantee

At-least-once delivery here specifically means: a batch that the
forwarder believes was not committed (because it never received a
committed `Ack`, due to any failure between send and ack) may be resent,
and the collector's dedup window makes that resend safe **as long as the
resend happens while the original `BatchId` is still within the dedup
window's bounded capacity.** This protocol does not claim exactly-once
delivery, and does not claim safety for a resend so delayed, or so far
behind so many other batches, that the original `BatchId` has already
been evicted from the dedup window.

## 9. Schema exchange

Schema fingerprints let a receiver recognize a schema it has already seen
without needing it inline on every batch. Two distinct exchange paths
exist:

1. **Reactive.** Either side sends `SchemaRequest{SchemaFingerprint}` at
   any point during steady state. The receiver looks up the fingerprint
   (via an application-supplied resolver) and replies
   `SchemaResponse{SchemaFingerprint, Found, SchemaBytes}`, **echoing the
   request frame's transaction number.** A requester correlates the
   response to its request by that echoed transaction number.
2. **Proactive, at handshake only.** If a `HelloAck` lists
   `UnknownSchemaFingerprints` (fingerprints the collector does not
   recognize among the offerer's `KnownSchemaFingerprints`), the offerer
   immediately sends one `SchemaResponse` per listed fingerprint —
   unsolicited, using a **freshly drawn** transaction number from its own
   sequence rather than echoing anything (there was no `SchemaRequest`
   frame to echo). This is a push, not a reply.

> **Implementation-status note.** Because a proactively-pushed
> `SchemaResponse` (path 2) uses a freshly drawn transaction number, a
> receiver correlating `SchemaResponse` frames only against transaction
> numbers it registered via an active `RequestSchemaAsync` call (as the
> reference implementation's receive loop does) has no pending
> registration to resolve it against. A conforming peer wishing to
> consume proactively-pushed schemas needs its own out-of-band handling
> for unsolicited `SchemaResponse` frames; this is not automatically
> wired into request/response correlation by the reference SDK.

## 10. `TypedBatch` payload contract

This is the type-fidelity core of `FWD-CONTRACT-v1`: a `TypedBatch`
frame's `ForwardBatchEnvelope.Payload` is a MessagePack-encoded
`ForwardLogBatch`, produced and consumed exclusively through
`ForwardLogBatchCodec.Encode`/`Decode` in the reference implementation.
Do not hand-roll this encoding independently of that codec — see §10.4.

### 10.1 `ForwardLogBatch`

```
BatchId      Guid       — the unit of deduplication and acknowledgement (§8.4)
CreatedAt    DateTimeOffset
Records      ForwardLogRecord[]
```

### 10.2 `ForwardLogRecord`

```
DeliveryId       string   (required)
AgentId          string   (required)
SourceType       string   (required)
SourceName       string   (required)
ProfileId        string?  (optional)
ProfileVersion   string?  (optional)
Platform         string?  (optional)
Hostname         string?  (optional)
RecordId         string   (required) — unique within its batch
CreatedAt        DateTimeOffset (required)
Fields           map<string, value>  (required; see §10.3)
```

The identity fields (`AgentId`, `SourceType`, `SourceName`, `ProfileId`,
`ProfileVersion`, `Platform`, `Hostname`) were split out of a single
`SourceId` string in a recent revision of the reference implementation.
This split is load-bearing for this protocol's no-wire-schema design
(ADR 0014, ADR 0015): because there is no schema carried on the wire, the
collector resolves which type-contract catalog entry governs a record's
`Fields` from this per-record identity tuple
`(SourceType, SourceName, ProfileId, ProfileVersion)`, not from anything
in the batch envelope.

### 10.3 Field value encoding

`Fields` values are restricted, after normalization, to exactly ten KQL
scalar types plus dynamic maps, dynamic arrays, and null — never an
upstream producer's internal event-object model. Each value is written
as either MessagePack `nil`, or a 2-element MessagePack array
`[tag, payload]`:

| Tag | CLR type | Payload encoding |
| --- | --- | --- |
| `0` | `bool` | MessagePack bool |
| `1` | `long` | MessagePack int |
| `2` | `double` | MessagePack float |
| `3` | `string` | MessagePack string |
| `4` | `DateTimeOffset` | MessagePack string, ISO 8601 round-trip (`"o"`) format |
| `5` | `TimeSpan` | MessagePack int — `Ticks` (100-nanosecond units) |
| `6` | `Guid` | MessagePack string, `"D"` format (`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`) — **note this is the standard hyphenated string form, unlike the raw 16-byte non-standard layout used for frame-level `Guid` fields in §4** |
| `7` | `decimal` | MessagePack string, invariant-culture decimal literal |
| `8` | map | MessagePack array `[8, <msgpack map header + entries, each value recursively tagged>]` |
| `9` | array | MessagePack array `[9, <msgpack array header + entries, each value recursively tagged>]` |

This explicit tagging exists because plain/"typeless" MessagePack
inference is lossy at the boundary of an `object`-typed field —
`DateTimeOffset` and `decimal`, notably, would otherwise both decode as
ambiguous strings with no way to tell them apart or from a plain string
field.

### 10.4 Normalization rules (encode-time)

Before encoding, every field value is normalized to one of the tagged
types above (`ForwardValueNormalizer`), so nothing reaches the wire
outside the ten-type-plus-dynamic-container-plus-null set:

| Input CLR type | Normalizes to |
| --- | --- |
| `null` | `null` |
| `bool` | `bool` (unchanged) |
| `long`, `int`, `short`, `sbyte`, `byte`, `ushort`, `uint` | `long` (widened) |
| `ulong` | `long` (checked cast — **throws if the value exceeds `long.MaxValue`**) |
| `double`, `float` | `double` (widened) |
| `string` | `string` (unchanged) |
| `DateTimeOffset` | `DateTimeOffset` (unchanged) |
| `DateTime` | `DateTimeOffset` — `Local` kind converts with its offset; any other kind (`Utc`, `Unspecified`) is treated as UTC |
| `TimeSpan` | `TimeSpan` (unchanged) |
| `Guid` | `Guid` (unchanged) |
| `decimal` | `decimal` (unchanged) |
| `IReadOnlyDictionary<string, object?>` | recursively normalized map |
| any other `IEnumerable` (except `string`) | recursively normalized array |
| anything else | rejected — `NotSupportedException` |

A conforming encoder MUST reject a field value it cannot normalize to
this set rather than pass it through, coerce it to a string, or drop it
silently. There is no escape hatch or "raw passthrough" field type in
this contract; if a producer needs to carry data this model cannot
express, that is a signal to fix the producer or extend a future contract
version, not to smuggle it through as an opaque blob inside a typed
batch (a `RawEnvelope` batch, §11, exists for that).

## 11. `RawEnvelope` payload

A `RawEnvelope` frame's `ForwardBatchEnvelope.Payload` is **opaque to
this protocol.** `FWD-CONTRACT-v1` deliberately does not define its
contents; the payload format is a private agreement between the specific
forwarder and collector implementations using it, negotiated entirely
outside this specification. This repository's own `ForwarderTransport`/
`ForwarderInput`, for example, currently send a MessagePack-encoded
`DeliveryBatch`/`DeliveryRecord` (this repository's own transitional
domain type, described in ADR 0011's "FORWARDER compatibility framing"
and unrelated to `ForwardLogBatch`) as a `RawEnvelope` payload — that is
this repository's private choice, not part of `FWD-CONTRACT-v1`. A
`RawEnvelope` batch still participates fully in this protocol's batch
identity, acknowledgement, credit-window, and deduplication mechanics
(§7-8); only the payload bytes inside the envelope are outside this
specification's scope.

`RawEnvelope` is the intended path for a source parsed and typed at the
**collector** tier rather than the agent tier (see ADR 0011).

## 12. Relationship to RELP and to fluentd's Forward protocol

| Aspect | RELP | fluentd Forward | DeltaZulu.Forward |
| --- | --- | --- | --- |
| Framing | Text: `txnr command datalen data\n` | MessagePack-RPC-style event stream | Fixed 16-byte binary header + CRC-32 |
| Command model | Text verbs (`open`, `close`, `syslog`) | Message modes (`Message`/`Forward`/`PackedForward`) | First-class typed frame types (§6), no text verbs |
| Handshake | Offer/capability text exchange | `HELO`/`PING`/`PONG` with optional shared-key auth | Typed `Hello`/`HelloAck`: catalog version, schema fingerprints, compression, windows |
| Payload | Syslog message text | MessagePack event records `[tag, time, record]` | MessagePack `ForwardLogBatch` (typed) or opaque bytes (raw) |
| Ack | `rsp` frame, one at a time (single-flight) | `{"ack": "..."}` optional per-chunk ack | `Ack` frame, many in flight under a credit window |
| Dedup | None built in | Optional, via chunk ack + `chunk` option | Built in: bounded, session-spanning, batch-UUID-keyed |
| Backpressure | None built in | None built in | `Control` frames: window adjustment, throttle |
| Integrity | None built in | None built in | CRC-32 per frame payload |
| Interop goal | rsyslog/librelp ecosystem | fluentd/fluent-bit ecosystem | None — proprietary, DeltaZulu-only |

DeltaZulu.Forward **harvested** from RELP: application-layer
acknowledgement bound to durable commit, per-frame transaction numbers,
negotiated windowing, an offer/capability handshake, octet-counted
binary-safe framing, and session-resumption vocabulary. It **dropped**:
text command verbs, syslog payload assumptions, `librelp` wire
compatibility and its TLS layering, and the space-separated header
grammar. It **added beyond RELP**: the typed handshake fields, first-class
frame types for schema exchange/dead-lettering/control, and the
dedup window. It takes only its *name* from fluentd's Forward protocol,
sharing no wire format with it — see §1.

A prior iteration of the reference library implemented literal RELP
framing directly; that design was superseded specifically because
RELP's text framing and `librelp` compatibility exist to interoperate
with the rsyslog ecosystem, and DeltaZulu.Forward has no such interop
goal (§1.1).

## 13. Security considerations

- **TLS is optional and external to this protocol's own handshake.**
  When used, it wraps the TCP stream beneath the framing described here;
  this specification defines no protocol-native authentication,
  confidentiality, or integrity beyond what TLS (if enabled) provides at
  the transport layer.
- **CRC-32 is corruption detection, not a security control.** It is not
  cryptographically strong and provides no protection against a
  deliberate, capable adversary tampering with frame contents — do not
  rely on it for anything beyond catching accidental bit-level
  corruption. Confidentiality and tamper resistance against an active
  attacker require TLS.
- **`RejectReason` and `Ack.Detail` are free-text.** Implementations
  SHOULD NOT echo untrusted internal error detail (stack traces,
  internal paths, credentials) into these fields, since they cross a
  trust boundary to the peer.
- **No protocol-native authentication of either endpoint.** Session
  identity (`SessionId`) is assigned by the collector at handshake and is
  not itself a credential; endpoint authentication, if required, is a
  TLS (mutual-TLS client certificate) or network-layer concern, not
  something this frame format carries.

## 14. Non-goals

- Wire compatibility with RELP/`librelp` or with fluentd/fluent-bit's
  Forward protocol. Neither is a goal at any point on this channel (§1,
  §12).
- A degraded/fallback wire format for outage scenarios (§1.1).
- Exactly-once delivery (§8.5).
- A schema-bytes format or type-contract-catalog encoding — `SchemaBytes`
  (§6.5) is treated as an opaque blob by this protocol version; its
  content is defined by the catalog layer above it, not here.

## Appendix A — Reference implementation map

| Concept | Type (`DeltaZulu.Forward` namespace) |
| --- | --- |
| Frame header | `ForwardFrameHeader` |
| Frame type / flags enums | `ForwardFrameType`, `ForwardFrameFlags` |
| Frame read/parse | `ForwardFrameReader`, `ForwardParser`, `ForwardParserOptions` |
| Frame write | `ForwardFrameTx` |
| Received frame | `ForwardFrameRx` |
| Connection (transport) | `ForwardConnection` |
| Session (protocol state machine) | `ForwardSession`, `ForwardSessionOptions` |
| Handshake payloads | `ForwardHandshakeOffer`, `ForwardHandshakeAck` |
| Batch envelope | `ForwardBatchEnvelope` |
| Schema exchange payloads | `ForwardSchemaRequest`, `ForwardSchemaResponse` |
| Dead letter payload | `ForwardDeadLetter` |
| Control payload | `ForwardControlMessage`, `ForwardControlType` |
| Ack payload | `ForwardAckOutcome`, `ForwardAckCodec` |
| Credit window | `ForwardCreditWindow` |
| Dedup window | `ForwardDedupWindow` |
| Transaction number | `TxNr` |
| Typed batch model | `ForwardLogBatch`, `ForwardLogRecord` |
| Typed batch codec | `ForwardLogBatchCodec` |
| Field normalization | `ForwardValueNormalizer` |
| Field wire tagging | `ForwardValueTag`, `ForwardObjectFormatter` |
| Compression enum (negotiated, not enforced) | `ForwardCompression` |

## Appendix B — Relationship to this repository's ADRs

- [ADR 0011](adr/0011-deltazulu-forward-transport.md) — names and adopts
  DeltaZulu.Forward as this repository's target transport; describes the
  design rationale this specification's §1/§12 draw on.
- [ADR 0014](adr/0014-messagepack-wire-supersedes-avro.md) — establishes
  that the wire payload is MessagePack, not Avro; §10 of this document is
  the detailed contract that decision points at.
- [ADR 0015](adr/0015-no-arrow-catalog-typed-records.md) — the collector
  decodes directly to catalog-typed records with no Arrow layer; §10.2's
  note on record-identity-driven catalog resolution is why that works
  without a wire-carried schema.

This specification describes the protocol as implemented by the
`DeltaZulu.Forward` library; it does not itself decide anything already
decided by those ADRs, and a change to this document that contradicts one
of them should be treated as a bug in this document, not a silent
protocol change.
