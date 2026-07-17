# ADR 0007: Profile parse/filter split and unified PDAG

## Status

Accepted.

Supersedes ADR 0004's guidance where it could imply that one Rx.Kql profile
query owns both plaintext parsing and filtering. ADR 0004 remains authoritative
for Rx.Kql capability within `filter.query`.

## Context

Plaintext parsing needs deterministic compilation, one traversal per record, and
explicit unknown-event behavior. Rx.Kql is the filter execution engine, not a
restricted parser-rule compiler.

## Decision

Add optional backwards-compatible `parse.query`; profiles without it remain
valid and do not need a schema-version bump. V1 accepts only
`<table> | normalize <field> with (...)`. Every rule contains exactly one
`topic.<name>` tag and one profile initially has one topic. Compatible fragments
are ordered by profile ID, version, and rule ordinal, compiled eagerly into one
immutable Normalize PDAG generation per domain, and atomically swapped.

`filter.query` remains compiled and run by `DeltaZulu.Agent.Filter` through
Rx.Kql. It selects/filter outputs; it does not parse plaintext.

## Consequences

- A plaintext record invokes Normalize once and keeps raw content and all tags.
- A recognized rule supplies an envelope logical topic; no per-topic physical
  stream is created.
- No-match is `Unrecognized`, not a parser error, and remains dispatchable by
  catch-all raw-message filters.
- Failed replacement compilation leaves the previous generation active.
