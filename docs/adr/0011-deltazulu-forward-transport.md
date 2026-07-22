# ADR 0011: Transport naming and design — DeltaZulu.Forward

## Status

Accepted. Target design; not yet implemented (see ROADMAP.md Phase 12a).

## Context

The agent-to-collector transport previously had no ratified name and no
decision record distinguishing it from the RELP protocol literally spoken by
`DeltaZulu.Relp` today. ADR 0006 assigns `DeltaZulu.Relp` ownership of RELP
framing, sessions, transactions, and acknowledgements for the current,
transitional local-validation path (see `docs/RELP_RECEIVER_SETUP.md`), but
does not decide what the long-term, Avro-carrying, agent-to-collector
transport is or what it is called. Reusing literal RELP long-term would carry
legacy constraints (text command verbs, syslog payload assumptions, librelp/
rsyslog wire compatibility) that the Avro payload (ADR 0010) already forfeits
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

One Avro batch per frame; the ack means durable acceptance of that batch;
batches are never split across frames and frames never carry multiple
independently committable batches. Every batch carries a UUID; the collector
maintains a bounded, session-spanning dedup window applied before decode, so
at-least-once delivery's guaranteed duplicates never reach a typed detection
pipeline as double-counted events.

Interop with rsyslog-world or fluentd peers is a non-goal on this channel. Raw
ingestion from such sources is a separate input adapter feeding Parse, and may
continue to use literal RELP (`DeltaZulu.Forward`) as a receiving protocol for
that adapter.

## Alternatives rejected

- **Pure FORWARDER via a librelp wrapper**: carries legacy constraints the Avro
  payload already forfeits the interop those constraints exist for.
- **gRPC streaming**: rejected for agent dependency weight, opaque
  flow-control tuning, and no native ack-on-commit semantics.
- **QUIC via `System.Net.Quic`**: deferred on enterprise middlebox traversal
  and operational maturity; reconsider if roaming-endpoint requirements
  emerge.
- **fluentd Forward protocol itself**: the naming inspiration, but its
  MessagePack payload model reintroduces the type floor ADR 0010 exists to
  avoid; adopting its wire format without its payload format would be
  compatibility theater.

## Consequences

- Literal RELP narrows from "the agent-to-collector transport" to "a
  legacy/rsyslog-world peer input adapter and older local-validation transport"
  (`docs/RELP_RECEIVER_SETUP.md`). Current checked-in daemon configuration uses
  `forwarder:`/`transport: forwarder` compatibility framing while the target
  binary Avro DeltaZulu.Forward protocol remains Phase 12a work. ADR 0006
  remains accepted for any literal-RELP peer input path.
- The protocol state machine (retransmit-after-reconnect races, cross-session
  duplicates, txnr wraparound, half-open detection, window exhaustion,
  shutdown with unacked frames) is a separately testable component with its
  own harness budget — the highest-maintenance of DeltaZulu's owned
  implementations (Parse, the catalog, Forward).

## Revisit triggers

None recorded yet; add here if fleet roaming/middlebox requirements make QUIC
attractive, or if measured Forward overhead versus plain RELP-derived text framing proves
material.
