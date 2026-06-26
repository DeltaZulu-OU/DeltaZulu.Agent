# Implementation status

## Implemented in this archive

- Project split for Agent.
- .NET 10 project targets.
- `System.Reactive` based inputs.
- No internal observable classes.
- Resource profile YAML model and loader.
- KQL profile executor seam using `Microsoft.Rx.Kql`.
- NDJSON file and console sinks.
- Syslog TCP/file input with lightweight parsing.
- CSV file input using CsvHelper.
- Auditd parser and basic event assembler.
- Windows Event Log, EVTX, ETL, and ETW inputs using the original Tx.Windows approach.
- Sample profiles for sshd, PAM, auditd execve, Windows Security, Sysmon, and ETW kernel process.
- Forwarder project with RELP-neutral delivery records, buffered chunk sending, demo TCP transport, a console-printing demo ACK server, and buffered-forwarder health snapshots.

## DeltaZulu.Buffer

Local durable buffering library, implemented and tested:

- Binary chunk format with SHA-256 checksums and length-prefixed records.
- `ChunkBuilder` with streaming `IncrementalHash`, `MemoryStream`, and `BinaryPrimitives`.
- `FileChunkStore` with atomic rename-based state transitions and symlink protection.
- `DispatchWorker` with `PriorityQueue`-based retry scheduling.
- `ExponentialBackoffRetryScheduler` with ±25% jitter.
- `BackpressureController` with four states and three full policies (Block, RejectNewest, DropOldest).
- `FileSystemRecoveryManager` for crash recovery on startup.
- `BufferMetricsCounter` with lock-free `Interlocked` counters.
- `BufferEventBroadcaster` with lock-free `ImmutableArray` observer list.
- `DeltaZuluBufferHost<T>` as the public lifecycle owner with `IObservable<BufferEvent>`.
- `JsonRecordSerializer<T>` as a default `IRecordSerializer<T>`.
- Unit tests: ChunkBuilder, ChunkFormat, BackpressureController, BufferMetricsCounter, BufferEventBroadcaster, ExponentialBackoffRetryScheduler, FileChunkStore, JsonRecordSerializer.
- Integration tests: end-to-end write-dispatch-ACK, flush on stop, permanent failure dead-lettering, record-too-large rejection.

## Recently implemented

- Auditd assembler hardening: EOE and PROCTITLE records trigger immediate event completion, malformed audit lines are skipped gracefully, hex-encoded PATH names are decoded, multi-instance record types (PATH, SOCKADDR, OBJ_PID, FD_PAIR, BPRM_FCAPS) always produce arrays.
- Metadata preservation: KQL projections that omit `_metadata` from `project` no longer lose delivery identity fields (collectorId, sourceType, sourceName, platform, hostname). The KQL executor captures source metadata and injects it into output records as a fallback.
- Golden fixture tests: raw-input to NDJSON-output tests for syslog (RFC 5424, RFC 3164, unstructured), auditd (single record, multi-record EXECVE, EOE completion, PROCTITLE completion, hex PATH decoding, malformed line handling), CSV (type coercion, file roundtrip), and NDJSON envelope structure.
- Forwarder failure scenario tests: transient failure with retry scheduling, permanent failure with dead-lettering, record-count mismatch detection, delivery record identity fallback chains, serializer roundtrip, and health observation completeness.

## Not implemented yet

- Daemon, service lifecycle, installer, rsyslog/syslog-ng snippets.
- Production RELP/TLS adapter over the forwarder transport port.
- Enrichment and resource-local state providers.
- Full LAUREL-level auditd decoding and process tracking.
- Profile hot reload.
- Build validation in this environment.

## Explicitly out of scope

- DuckDB.
- Server-canonical normalization at the edge.
- A built-in syslog daemon replacement.
