# ADR 0011: Transport naming and design — DeltaZulu.Forward

## Status

Accepted. Target design; not yet implemented (see ROADMAP.md Phase 12a).

> **Wire-format note ([ADR 0014](0014-messagepack-wire-supersedes-avro.md)):**
> this ADR's typed-batch payload is MessagePack (a `ForwardLogBatch`,
> matching `DeltaZulu.Forward`'s own implementation), not Avro. Below,
> "Avro" describes this ADR's now-superseded original assumption; read
> ADR 0014 for the current decision. The framing, handshake, windowing, and
> dedup design in this ADR is otherwise unaffected.

## Context

The agent-to-collector transport previously had no ratified name and no
decision record distinguishing it from the RELP protocol literally spoken by
`DeltaZulu.Relp` at the time this ADR was written. (`DeltaZulu.Relp` was
subsequently renamed to `DeltaZulu.Forward` upstream and no longer exists as
a distinct package; see the Decision below for this ADR's own transport
naming, which the upstream rename now matches.) ADR 0006 assigns
`DeltaZulu.Relp` ownership of RELP framing, sessions, transactions, and
acknowledgements for a prospective literal-RELP local-validation path, but
no literal-RELP client or receiver was ever built against that decision, and
it does not decide what the long-term, typed, agent-to-collector transport
is or what it is called. Reusing literal RELP long-term would carry legacy
constraints (text command verbs, syslog payload assumptions, librelp/rsyslog
wire compatibility) that the typed payload (ADR 0010/0014) already forfeits
the interop those constraints exist for.

## Decision

The transport is named **DeltaZulu.Forward**, following the fluentd Forward
protocol convention: a product-scoped name for a product-scoped reliable
forwarding protocol. It is a proprietary reliable framing protocol
**implemented in `DeltaZulu.Pipeline`** (not delegated to an external
protocol library), derived from RELP's design but not wire-compatible with
it.

Harvested from RELP: application-layer acknowledgments bound to durable
commit, per-frame transaction numbers, negotiated windowing, an offer/
capability handshake, octet-counted binary-safe framing, and session-resumption
semantics defining the at-least-once contract. Dropped: text command verbs,
syslog payload assumptions, librelp compatibility and its TLS layering, the
SP-separated header grammar — replaced by a fixed binary header (type, txnr,
length, flags) with TLS as plain stream transport beneath. Added beyond RELP:
a typed handshake negotiating catalog version, known schema fingerprints,
compression, and dedup-window size; first-class frame types (typed-batch,
raw-envelope, schema-request/response, dead-letter-forward, control); explicit
backpressure signaling via window adjustment or throttle frames.

One MessagePack-encoded `ForwardLogBatch` (ADR 0014) per frame; the ack
means durable acceptance of that batch; batches are never split across
frames and frames never carry multiple independently committable batches.
Every batch carries a UUID; the collector maintains a bounded,
session-spanning dedup window applied before decode, so at-least-once
delivery's guaranteed duplicates never reach a typed detection
pipeline as double-counted events.

Interop with rsyslog-world or fluentd peers is a non-goal on this channel. Raw
ingestion from such sources is a separate input adapter feeding Parse, and may
use literal RELP as a receiving protocol for that adapter, if and when it is
built — no such adapter exists in this repository today.

## Alternatives rejected

- **Pure FORWARDER via a librelp wrapper**: carries legacy constraints the
  typed payload already forfeits the interop those constraints exist for.
- **gRPC streaming**: rejected for agent dependency weight, opaque
  flow-control tuning, and no native ack-on-commit semantics.
- **QUIC via `System.Net.Quic`**: deferred on enterprise middlebox traversal
  and operational maturity; reconsider if roaming-endpoint requirements
  emerge.
- **fluentd Forward protocol itself**: the naming inspiration, but not
  adopted wholesale — its wire framing (a MessagePack-RPC-based event
  stream) does not provide the ack-on-durable-commit, negotiated windowing,
  or dedup-window semantics harvested from RELP; adopting its framing
  without those semantics would be compatibility theater. (Its choice of
  MessagePack for the payload is not the distinguishing factor —
  DeltaZulu.Forward independently uses MessagePack too, per ADR 0014; the
  two protocols differ in framing and session semantics, not payload
  serialization.)

## Consequences

- Literal RELP narrows from "the agent-to-collector transport" to, at most, a
  possible future legacy/rsyslog-world peer input adapter; no literal-RELP
  client or receiver was ever built. Current checked-in daemon configuration
  uses `forwarder:`/`transport: forwarder` compatibility framing over
  DeltaZulu.Forward while the target binary, typed DeltaZulu.Forward
  protocol (MessagePack-encoded `ForwardLogBatch` batches, ADR 0014) remains
  Phase 12a work. ADR 0006 remains accepted for any future literal-RELP peer
  input path.
- The protocol state machine (retransmit-after-reconnect races, cross-session
  duplicates, txnr wraparound, half-open detection, window exhaustion,
  shutdown with unacked frames) is a separately testable component with its
  own harness budget — the highest-maintenance of DeltaZulu's owned
  implementations (Parse, the catalog, Forward).

## Revisit triggers

None recorded yet; add here if fleet roaming/middlebox requirements make QUIC
attractive, or if measured Forward overhead versus plain RELP-derived text framing proves
material.

## Related decisions

- [`FORWARD_PROTOCOL_SPECIFICATION.md`](../FORWARD_PROTOCOL_SPECIFICATION.md)
  is the standalone wire-protocol specification (`FWD-CONTRACT-v1`) this
  ADR's design rationale is written up as: frame format, handshake,
  reliability semantics, and the `TypedBatch` payload contract, extracted
  from the reference `DeltaZulu.Forward` implementation.
