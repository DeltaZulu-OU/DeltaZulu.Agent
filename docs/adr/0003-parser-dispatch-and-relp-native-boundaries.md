# ADR 0003: Parser dispatch and FORWARDER native boundaries

## Status

Superseded by [ADR 0006: Input materialization boundaries and FORWARDER ownership](0006-input-materialization-and-relp-ownership.md) and [ADR 0007: Profile parse/filter split and unified PDAG](0007-parse-filter-split-and-unified-pdag.md).

> Historical decision retained for context. Its managed-FORWARDER conclusion is preserved, but its input-layer typed-parser and parser-dispatch design is replaced by Normalize-owned PDAG parsing.

## Context

The agent needs better precision and performance for Linux text inputs such as
syslog and auditd, and future text/file sources may include Netlogon, IIS W3C,
DNS debug logs, application key/value logs, CSV-like records, and other
line-oriented formats.

These needs overlap with two different implementation questions:

- **Parsing and dispatch**: how the agent selects and executes a parser for many
  source-specific log grammars.
- **FORWARDER transport**: how the agent forwards already parsed and filtered records
  to the server.

They should not be coupled. A SWIG wrapper over `librelp` can only help the FORWARDER
transport boundary; it does not solve parser selection, source framing, or field
dictionary construction. A parser dispatch graph can help text-input precision,
but it does not replace FORWARDER delivery, TLS, retry, or durable buffering.

The current repository already uses a managed FORWARDER transport adapter over the
`DeltaZulu.Forward` submodule for forwarding, while parser code is owned by input
adapters. The architecture also requires parsers to expose source-native fields
and leave platform-canonical normalization to the server.

## Decision

Implement parser selection and parsing in managed C# as a source-input concern.
Do **not** introduce a SWIG-based `librelp` layer as a prerequisite for parser
work.

The parser architecture should be a managed parser dispatch graph, inspired by
rsyslog/syslog-ng parser-chain and pattern-database ideas but implemented in
C# with DeltaZulu-specific contracts:

```text
input framing
  -> parser dispatch graph
  -> typed source parser
  -> source-native dictionary
  -> profile KQL filter/projection
  -> enrichment/output
```

Use deterministic typed parsers for first-class formats such as syslog, auditd,
IIS W3C, DNS debug logs, Netlogon logs, JSON lines, CSV/delimited rows, EVTX,
ETL, ETW, and Windows Event Log. Shared helpers may parse reusable primitives
such as key/value payloads and delimited rows, but they must not become the only
parser abstraction.

Keep FORWARDER transport behind `IDeliveryTransport`. Revisit `librelp` only if FORWARDER
wire-compatibility, TLS behavior, throughput, or protocol maintenance evidence
shows the managed `DeltaZulu.Forward` adapter is insufficient.

## Rationale

### Why managed parser dispatch instead of SWIG/librelp?

Parser dispatch and FORWARDER are different layers. Netlogon, IIS, DNS, auditd, and
syslog precision require input framing, parser selection, source-specific
grammars, field provenance, parser health counters, and source-native output
dictionaries. A native FORWARDER binding does not address those requirements.

Managed parser dispatch also keeps the hot path portable and testable in the
existing .NET test suite. The agent can benchmark parser candidates, measure
allocation/CPU behavior, and keep parser failures in normal source-health and
pipeline-health observations without crossing a native interop boundary.

### Why not use only shared dictionary helpers?

Shared key/value and delimited-field helpers are useful primitives, but many log
families have source-specific framing:

- auditd groups multiple physical records into one event by audit ID;
- IIS W3C rows depend on `#Fields:` headers;
- Netlogon logs have timestamp/thread/context prefixes before message payloads;
- DNS debug logs have positional sections and source-specific tokens;
- syslog has an envelope plus an application payload.

The dispatch graph should choose a typed parser first, then let that parser use
common helpers where appropriate.

### Why keep FORWARDER managed for now?

The current forwarding boundary already separates durable buffering from the
transport adapter. `IDeliveryTransport` is the seam where a future `librelp`
adapter could be introduced without changing parsers or source inputs. Until
measurements prove the managed FORWARDER path is inadequate, a SWIG dependency would
add native packaging, ABI, TLS, platform, and diagnostic complexity without
advancing parser correctness.

## Consequences

- Parser work proceeds in C# with typed parser contracts and deterministic tests.
- `LogFieldNormalizer`-style helpers, if kept, remain low-level utilities used by
  typed parsers rather than the parser architecture itself.
- Parser dispatch should expose parser name/version, parser result, parser
  failure counters, and enough metadata for source health observations.
- A future SWIG/librelp adapter remains possible behind `IDeliveryTransport`, but
  it is not on the critical path for syslog/auditd/file parsing improvements.
- New input families must define their framing and parser-selection rules before
  adding broad generic extraction logic.

## Follow-up work

- Define `ILogRecordParser`, `LogParseContext`, `ParsedLogRecord`, and
  `ParserDispatchGraph` contracts in the input layer.
- Move syslog and auditd parsing behind those contracts.
- Add typed parsers for IIS W3C, Netlogon, and DNS debug logs before adding any
  pattern-language fallback.
- Add parser health counters for parse success, raw-only fallback, malformed
  records, and parser exceptions.
- Benchmark managed FORWARDER and parser dispatch separately; evaluate `librelp`
  only with transport-specific evidence.
