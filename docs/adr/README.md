# Architecture decision records

| ADR | Status | Decision |
| --- | --- | --- |
| [0001](0001-windows-eventing-library-boundaries.md) | Accepted | Windows eventing library boundaries. |
| [0002](0002-windows-sid-observation-boundary.md) | Accepted | Windows SID observation boundary. |
| [0003: Profile KQL preserves source event shape](0003-profile-kql-preserves-source-event-shape.md) | Superseded | Historical KQL projection guidance; superseded by ADR 0004. |
| [0003: Parser dispatch and RELP native boundaries](0003-parser-dispatch-and-relp-native-boundaries.md) | Superseded | Historical parser-dispatch proposal; superseded by ADRs 0006 and 0007. |
| [0004](0004-kql-capability-alignment.md) | Partially superseded | Rx.Kql capability remains accepted; parse/filter ownership is superseded by ADR 0007. |
| [0005](0005-single-pipeline-assembly-boundaries.md) | Accepted | One Pipeline assembly with internal boundaries. |
| [0006](0006-input-materialization-and-relp-ownership.md) | Accepted | Acquisition/materialization boundaries and RELP ownership. |
| [0007](0007-parse-filter-split-and-unified-pdag.md) | Accepted | Optional parse query, separate filtering, and unified PDAG. |
| [0008](0008-localstream-durability-and-no-production-multiplexer.md) | Accepted | LocalStream durability boundaries and no daemon multiplexer. |
| [0009](0009-unrecognized-events-and-blindness-measurement.md) | Accepted | Unknown-event preservation and blindness measures. |
| [0010](0010-schema-registry-avro-arrow-and-ndjson-edge-dialect.md) | Accepted | Schema registry, Avro wire, Arrow batches, and governed NDJSON edges. |

The repository contains two historical ADR files numbered 0003. Their filenames
disambiguate them; new ADRs use unique numbers.
