# DeltaZulu.Agent Test Plan

This repository validates business logic with fast unit tests and avoids mocks for operating-system components such as auditd, journald, ETW, and event logs. OS integrations should be covered later by opt-in integration tests that run only on compatible hosts with fixture files or real services.

## Project-by-project plan

| Project | Current test focus | High-ROI scenarios | Deferred coverage |
| --- | --- | --- | --- |
| `DeltaZulu.Agent.Core` | Pure event and dictionary helpers. | Dictionary coercion preserves case-insensitive field access, safely handles nullable values, and emits KQL-ready dictionaries without nulls. | Reactive pipeline lifecycle tests with in-memory observables and sinks. |
| `DeltaZulu.Agent.Inputs.Auditd` | Parser and assembler business logic using literal audit lines. | Audit line prefix validation, scalar coercion, hex argument decoding, record grouping by audit ID, ordered `ARGV`, multi-record fields such as `PATH`, and one-shot flush behavior. | Reading `/var/log/audit/audit.log`, tailing files, or interacting with auditd. |
| `DeltaZulu.Agent.Inputs.Syslog` | Dependency-free parser behavior using literal RFC 3164, RFC 5424, and unstructured messages. | Priority decoding, hostname/process extraction, source address preservation, key/value extraction, and raw-message preservation. | TCP listener behavior, journald, syslog daemon configuration, and socket-level error handling. |
| `DeltaZulu.Agent.Inputs.Files` | Not implemented in this pass. | CSV row-to-event behavior using temporary files and culture-invariant parsing. | Filesystem watcher behavior and long-running tail behavior. |
| `DeltaZulu.Agent.Inputs.Windows` | Not implemented in this pass because the project targets `net10.0-windows` and depends on Windows eventing packages. | Pure mapping helpers such as `WindowsSourceEventMapper` and dictionary extension behavior. | ETW sessions, live Windows Event Log subscriptions, and EVTX/ETL host dependencies. |
| `DeltaZulu.Agent.Kql` | Not implemented in this pass because it depends on the KQL runtime package. | Scalar helper behavior and profile executor no-match/error behavior with in-memory events. | Query engine internals and package behavior outside the repository boundary. |
| `DeltaZulu.Agent.Outputs.Ndjson` | Serializer and error-record business logic. | Property-name preservation, null omission, compact single-line JSON settings, and exception-to-error mapping. | Console/file sink I/O failures and file permission integration cases. |
| `DeltaZulu.Agent.Profiles` | Profile validation business rules. | Minimal valid profiles, unsupported languages/formats, required fields, preservation requirements, and source-aware exception messages. | Full YAML fixture loading across every bundled production profile. |

## Execution strategy

1. Keep unit tests deterministic and host-neutral; tests should run without auditd, journald, ETW, event logs, or privileged services.
2. Keep unit coverage in the single `DeltaZulu.Agent.Tests` MSTest project, with one test class per source project or domain area.
3. Prefer literal representative log/profile samples for parser and validator tests.
4. Add fixture-based integration tests later under a separate naming convention or trait so CI can opt in explicitly.
5. Prioritize tests around field preservation, normalization boundaries, and error messages because these define compatibility with downstream ResourceQL processing.

## DeltaZulu.Buffer test coverage

Tests are in `tests/DeltaZulu.Buffer.Tests/` using MSTest.

| Test class | Focus |
| --- | --- |
| `ChunkBuilderTests` | Append, record count, rotation by count/age, seal validation, checksum roundtrip, reset, byte limit checks. |
| `ChunkFormatTests` | Checksum validation, corruption detection, record reading, invalid magic, empty chunk. |
| `BackpressureControllerTests` | All four states (Healthy/Degraded/Pressured/Full), boundary values, precedence rules. |
| `BufferMetricsCounterTests` | Counter increments, gauge updates, snapshot completeness, `AddDiskBytes` with positive/negative values. |
| `BufferEventBroadcasterTests` | Subscribe/unsubscribe, multiple observers, faulty observer isolation, completion notification. |
| `ExponentialBackoffRetrySchedulerTests` | Delay increase, max delay cap, retry exhaustion. |
| `FileChunkStoreTests` | Directory creation, seal/move/delete lifecycle, metadata JSON roundtrip, disk usage, quarantine, orphan handling. |
| `JsonRecordSerializerTests` | UTF-8 output, naming policy, dictionary serialization. |
| `BufferIntegrationTests` | End-to-end write-dispatch-ACK, flush on stop, event observation, rejected-stopping, permanent failure dead-lettering, record-too-large. |
| `BufferWriteResultTests` | `IsAccepted` for each status. |
| `BufferSnapshotTests` | Property preservation. |
| `BufferEventTests` | Create with/without chunk, timestamp presence. |
| `ChunkIdTests` | Uniqueness, equality, ToString. |
| `ChunkMetadataTests` | JSON round-trip. |
| `ChunkSendResultTests` | Status and error. |
| `RecoverySummaryTests` | Property preservation. |
| `DeltaZuluBufferOptionsTests` | Defaults and custom values. |
