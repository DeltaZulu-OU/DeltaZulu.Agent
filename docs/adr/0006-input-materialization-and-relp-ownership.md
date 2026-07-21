# ADR 0006: Input materialization boundaries and RELP ownership

## Status

Accepted.

## Context

Inputs had accumulated source-specific plaintext parsing and the RELP input
combined protocol mechanics with payload mapping. This obscures acquisition
responsibilities and duplicates dedicated protocol-library concerns.

`DeltaZulu.Normalize` was renamed to `DeltaZulu.Parse` by ADR 0013; references
below use the current name.

> **Scope note (ADR 0011):** the long-term agent-to-collector transport is
> DeltaZulu.Forward, a RELP-derived but non-wire-compatible protocol owned by
> `DeltaZulu.Pipeline` itself, not `DeltaZulu.Relp`. This ADR's RELP-ownership
> decision remains accepted for the current transitional local-validation
> transport (`docs/RELP_RECEIVER_SETUP.md`, `config/dzagent.yaml`
> `transport: relp`) and for any future rsyslog-world peer input adapter. It no
> longer describes the target production transport.

## Decision

Inputs acquire, frame, decode, and map records into distinct text or structured
contracts. `DeltaZulu.Parse` exclusively parses unstructured and
semi-structured plaintext. Structured native/deterministic sources bypass it.
RELP protocol framing, sessions, transactions, and acknowledgements belong to
`DeltaZulu.Relp`; Pipeline RELP adapters retain configuration, payload codecs,
mapping, and metrics only.

Syslog admission is intentionally minimal: bounded framing/decoding plus PRI
validation. It never assigns semantic syslog fields or drops a valid unknown
candidate merely because no Normalize rule matches.

## Consequences

- CSV, Event Log, EVTX, ETL, ETW, and structured MessagePack RELP payloads use
  the structured path.
- TCP/UDP/file/FIFO plaintext paths do not import application parsers.
- Auditd correlation remains an assembler after normalized record materialization.
- RELP payload type, rather than RELP itself, determines the materialization path.
