# ADR 0009: Unrecognized events and blindness measurement

## Status

Accepted.

## Context

A parser no-match, a filter that emits nothing, and an operational failure are
different facts. Treating them alike causes valid unknown logs to disappear and
makes coverage impossible to assess.

## Decision

Preserve every admitted plaintext no-match as an `Unrecognized` parsed event
with raw message and acquisition metadata. Record parser blindness only for an
admitted plaintext record submitted to Normalize with no match. Dispatch records
`NoCandidate` separately from successful `NoMatch`; parser/filter/output errors
are operational failures and are not blindness. Complete blindness is an
unrecognized plaintext event with zero filter outputs.

Use bounded-cardinality metrics and an optional bounded diagnostic store for
unknown-event fingerprints and representative samples.

## Consequences

- Valid unknown syslog and other plaintext can be caught by raw-message filters.
- Admission rejects (invalid framing, size, decode, or PRI) are measurable but
  excluded from parser blindness.
- Coverage metrics can identify parser gaps, filter-routing gaps, and complete
  local coverage gaps without exposing sensitive values in labels.
