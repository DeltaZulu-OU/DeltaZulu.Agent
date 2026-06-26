# Next steps before merging

The repository already contains the buffer library, RELP-neutral forwarder contracts, buffered forwarder sink, RELP.Net transport adapter, separate demo collector, golden host-neutral fixtures, auditd hardening, delivery identity preservation, and forwarder health diagnostics. External .NET 10 validation has completed successfully with no compilation or runtime issues, so the remaining merge blockers are now documentation alignment and production transport hardening.

## Immediate validation

1. Keep the local validation commands in `docs/TEST_PLAN.md` current so required SDK, submodule, and Windows-only target expectations are explicit.
2. Re-run restore, build, and tests on a .NET 10-capable host whenever dependencies, SDK versions, Windows input adapters, or KQL execution behavior change.
3. Record any new host-specific limitations discovered during validation, especially around Windows Event Log, EVTX, ETL, and ETW projects.

## Profile and fixture follow-up

4. Extend golden raw-input to NDJSON-output fixtures only where coverage is still host-neutral or a new regression is found. Current fixture coverage already includes syslog, auditd, CSV, and NDJSON envelope scenarios.
5. Treat Windows Event Log nested KQL access as validated for `EventData.*` payloads and `_metadata.*` fields.
6. Defer auditd and other source-family nested KQL probes, including `SYSCALL.SYSCALL` and `EXECVE.ARGV`, until their profile work resumes.
7. Decide whether any future source family needs flattened KQL row exposure only after source-specific validation proves it is required.
8. Continue hardening profile validation around malformed profile diagnostics while preserving metadata injection that protects delivery identity from user-controlled projections.

## Input hardening

9. Keep current auditd hardening focused on parser/assembler correctness; LAUREL-level enrichment and process tracking are future post-1.0 work and must not add server-side normalization to the edge.
10. Continue validating Windows Event Log named payload extraction against live Security, Sysmon, PowerShell, SMB, and Defender providers; documentation now includes profile examples and the adapter exposes named XML `EventData` fields.
11. Add host-gated integration coverage for Windows Event Log, EVTX, ETL, ETW, auditd, and future journald behavior only after the host-neutral test set remains stable.

## Forwarder hardening

12. Continue hardening the RELP.Net adapter behind `IForwarderTransport`.
13. Continue production TLS hardening: certificate policy is now represented in YAML/options, but the RELP.Net adapter still needs validated server-certificate callback support and receiver-side TLS validation.
14. Continue endpoint selection hardening: basic ordered endpoint failover is now wired through forwarder options and YAML CLI configuration, but jittered reconnect/backoff remains delegated to `DeltaZulu.Buffer` retry scheduling and production transports still need richer transient/permanent failure classification.
15. Keep operational receiver documentation current in `docs/RELP_RECEIVER_SETUP.md`, including rsyslog/syslog-ng plain RELP and TLS snippets as transport behavior is validated.
16. Keep exercising the existing buffered RELP forwarder path against `dzdemo-collector` for success, transient failure, retry, permanent failure, dead-letter, and restart recovery scenarios.

## Delivery correctness and operations

17. Tie future source checkpoint advancement to durable enqueue rather than network ACK.
18. Add profile hot reload once forwarding and checkpoint semantics are clear.
19. Add typed resource-local enrichment providers only after version 1.0 is stable and only where they can run without changing server-canonical normalization.

## Architecture discipline

20. Keep `DeltaZulu.Buffer` as the authoritative durability and backpressure layer.
21. Keep RELP.Net details behind the forwarder transport adapter.
22. Defer broad Clean/Onion project restructuring until the RELP adapter and daemon host prove which boundaries need extraction.
