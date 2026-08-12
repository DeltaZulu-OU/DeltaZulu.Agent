# Architecture decision records

| ADR | Status | Decision |
| --- | --- | --- |
| [0001](0001-windows-eventing-library-boundaries.md) | Accepted | Windows eventing library boundaries. |
| [0002](0002-windows-sid-observation-boundary.md) | Accepted | Windows SID observation boundary. |
| [0003: Profile KQL preserves source event shape](0003-profile-kql-preserves-source-event-shape.md) | Superseded | Historical KQL projection guidance; superseded by ADR 0004. |
| [0003: Parser dispatch and FORWARDER native boundaries](0003-parser-dispatch-and-relp-native-boundaries.md) | Superseded | Historical parser-dispatch proposal; superseded by ADRs 0006 and 0007. |
| [0004](0004-kql-capability-alignment.md) | Partially superseded | Rx.Kql capability remains accepted; parse/filter ownership is superseded by ADR 0007. |
| [0005](0005-single-pipeline-assembly-boundaries.md) | Accepted | One Pipeline assembly with internal boundaries. |
| [0006](0006-input-materialization-and-relp-ownership.md) | Accepted; transport scope narrowed by 0011 | Acquisition/materialization boundaries and FORWARDER ownership for the current transitional transport. |
| [0007](0007-parse-filter-split-and-unified-pdag.md) | Accepted | Optional parse query, separate filtering, and unified PDAG. |
| [0008](0008-localstream-durability-and-no-production-multiplexer.md) | Accepted | LocalStream durability boundaries and no daemon multiplexer. |
| [0009](0009-unrecognized-events-and-blindness-measurement.md) | Accepted | Unknown-event preservation and blindness measures. |
| [0010](0010-type-catalog-avro-arrow-and-ndjson-edge-dialect.md) | Accepted for catalog authority; wire format superseded by 0014, internal representation superseded by 0015 | Type-contract catalog and governed NDJSON edges; Avro wire and Arrow internal representation are historical only. |
| [0011](0011-deltazulu-forward-transport.md) | Accepted; wire-format wording corrected by 0014 | Transport naming and design: DeltaZulu.Forward, a FORWARDER-derived, non-wire-compatible protocol owned by Pipeline. |
| [0012](0012-proton-ingestion-intermediate-protocol.md) | Superseded in full by 0016 | Historical: Proton ingestion via a Kafka-API-compatible intermediate protocol; no bespoke Proton sink. |
| [0013](0013-parse-naming.md) | Accepted | Naming: DeltaZulu.Parse, renamed from DeltaZulu.Normalize. |
| [0014](0014-messagepack-wire-supersedes-avro.md) | Accepted | Agent-to-collector wire format is MessagePack (matching DeltaZulu.Forward's `ForwardLogBatchCodec`), not Avro; supersedes ADR 0010's wire choice. |
| [0015](0015-no-arrow-catalog-typed-records.md) | Accepted | No Arrow; the collector works directly with catalog-typed records. Supersedes ADR 0010's internal-representation choice and corrects ADR 0014. |
| [0016](0016-bespoke-proton-sink-supersedes-kafka-intermediate.md) | Accepted | A bespoke native Proton sink replaces the Kafka-API-compatible intermediate; no broker, no Python fallback. Supersedes ADR 0012 in full. |

The repository contains two historical ADR files numbered 0003. Their filenames
disambiguate them; new ADRs use unique numbers.
