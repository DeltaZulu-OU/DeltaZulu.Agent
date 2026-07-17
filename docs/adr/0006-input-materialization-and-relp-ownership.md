# ADR 0006: Input materialization boundaries and RELP ownership

## Status

Accepted.

## Context

Inputs had accumulated source-specific plaintext parsing and the RELP input
combined protocol mechanics with payload mapping. This obscures acquisition
responsibilities and duplicates dedicated protocol-library concerns.

## Decision

Inputs acquire, frame, decode, and map records into distinct text or structured
contracts. `DeltaZulu.Normalize` exclusively parses unstructured and
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
