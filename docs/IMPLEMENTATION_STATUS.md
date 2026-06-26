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
- Forwarder project with RELP-neutral delivery records, buffered chunk sending, a RELP.Net-backed transport adapter, and buffered-forwarder health snapshots. A separate demo collector executable receives RELP frames for local validation.

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
- Stable `DeliveryId` on `DeliveryRecord`: each call to `FromResourceOutput` generates a unique UUID delivery envelope ID, separate from the event-sourced `RecordId`, enabling at-least-once deduplication fields on the receiving server. The field is serialized and deserialized by `DeliveryRecordSerializer`.
- Forwarder health reporter: `ForwarderHealthReporter` emits `collector.forwarder.health` snapshots (buffer state, disk usage, record/chunk/batch counters, last activity) on a configurable timer into any `IResourceSink`.
- CLI health wiring: `--diagnostic-interval <seconds>` enables periodic health snapshot emission when using `forwarder` output. Health records go to stdout (NDJSON) or a file specified by `--diagnostic-file`. A final snapshot is always emitted after the pipeline completes. `--agent-id` stamps the agent identifier on health metadata.
- RELP endpoint failover groundwork: forwarder options now accept an ordered endpoint list, the CLI loads endpoint lists from YAML forwarder configuration files, and the RELP transport advances to the next endpoint after transient send/open failures while keeping retry scheduling in `DeltaZulu.Buffer`.
- RELP TLS policy groundwork: YAML forwarder configuration now carries certificate validation mode, optional server thumbprint allow-list, client certificate path/password, and expiry warning thresholds; the CLI passes these options into the RELP-neutral transport boundary for production TLS hardening.
- Operational RELP receiver documentation: `docs/RELP_RECEIVER_SETUP.md` now captures rsyslog and syslog-ng plain RELP/TLS snippets, payload preservation expectations, and the agent-side TLS configuration checklist.
- Windows Event Log named payload extraction: live Event Log records now parse XML `EventData` names into a dynamic `EventData` object and mirror named payload values as top-level fields for profile compatibility with Security, Sysmon, PowerShell, SMB, and Defender KQL examples.

## Not implemented yet

- Daemon, service lifecycle, and installer.
- RELP/TLS wire-level validation callback support remains dependent on the underlying RELP.Net surface; receiver snippets are documented but still require environment-specific validation against production rsyslog/syslog-ng builds.
- Enrichment and resource-local state providers.
- Full LAUREL-level auditd decoding and process tracking.
- Profile hot reload.
- Build validation in this environment; this container does not have the `dotnet` executable installed.

## Explicitly out of scope

- DuckDB.
- Server-canonical normalization at the edge.
- A built-in syslog daemon replacement.
