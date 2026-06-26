# Next steps before merging

The repository already contains the buffer library, RELP-neutral forwarder contracts, buffered forwarder sink, RELP.Net transport adapter, separate demo collector, golden host-neutral fixtures, auditd hardening, delivery identity preservation, and forwarder health diagnostics. The remaining merge blockers are now validation/documentation and production transport hardening.

## Immediate validation

1. Restore and build locally with the .NET 10 SDK.
2. Fix any `Microsoft.Rx.Kql` and `Tx.Windows` API compatibility issues exposed by a clean build.
3. Run the existing Agent and Buffer test projects on a .NET 10-capable host.
4. Keep the local validation commands in `docs/TEST_PLAN.md` current so required SDK, submodule, and Windows-only target expectations are explicit.
5. Record any host-specific limitations discovered during validation, especially around Windows Event Log, EVTX, ETL, and ETW projects.

## Profile and fixture follow-up

6. Extend golden raw-input to NDJSON-output fixtures only where coverage is still host-neutral or a new regression is found. Current fixture coverage already includes syslog, auditd, CSV, and NDJSON envelope scenarios.
7. Prove whether nested field access works in profile KQL for:
   - `EventData.TargetUserSid`,
   - `SYSCALL.SYSCALL`,
   - `EXECVE.ARGV`,
   - `_metadata.profileId`.
8. Decide whether KQL row exposure must be flattened for nested fields.
9. Continue hardening profile validation around malformed profile diagnostics while preserving metadata injection that protects delivery identity from user-controlled projections.

## Input hardening

10. Extend auditd decoding toward LAUREL-level behavior without adding server-side normalization to the edge.
11. Continue validating Windows Event Log named payload extraction against live Security, Sysmon, PowerShell, SMB, and Defender providers; documentation now includes profile examples and the adapter exposes named XML `EventData` fields.
12. Add host-gated integration coverage for Windows Event Log, EVTX, ETL, ETW, auditd, and future journald behavior only after the host-neutral test set remains stable.

## Forwarder hardening

13. Continue hardening the RELP.Net adapter behind `IForwarderTransport`.
14. Continue production TLS hardening: certificate policy is now represented in YAML/options, but the RELP.Net adapter still needs validated server-certificate callback support and receiver-side TLS validation.
15. Continue endpoint selection hardening: basic ordered endpoint failover is now wired through forwarder options and YAML CLI configuration, but jittered reconnect/backoff remains delegated to `DeltaZulu.Buffer` retry scheduling and production transports still need richer transient/permanent failure classification.
16. Keep operational receiver documentation current in `docs/RELP_RECEIVER_SETUP.md`, including rsyslog/syslog-ng plain RELP and TLS snippets as transport behavior is validated.
17. Keep exercising the existing buffered RELP forwarder path against `dzdemo-collector` for success, transient failure, retry, permanent failure, dead-letter, and restart recovery scenarios.

## Delivery correctness and operations

18. Tie future source checkpoint advancement to durable enqueue rather than network ACK.
19. Add profile hot reload once forwarding and checkpoint semantics are clear.
20. Add typed resource-local enrichment providers where they can run without changing server-canonical normalization.

## Architecture discipline

21. Keep `DeltaZulu.Buffer` as the authoritative durability and backpressure layer.
22. Keep RELP.Net details behind the forwarder transport adapter.
23. Defer broad Clean/Onion project restructuring until the RELP adapter and daemon host prove which boundaries need extraction.
