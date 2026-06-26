# Next steps before merging

The repository already contains the buffer library, RELP-neutral forwarder contracts, buffered forwarder sink, RELP.Net transport adapter, and a separate demo collector. The remaining merge blockers are validation, fixture coverage, and production transport hardening.

## Immediate validation

1. Restore and build locally with the .NET 10 SDK.
2. Fix any `Microsoft.Rx.Kql` and `Tx.Windows` API compatibility issues exposed by the build.
3. Run the existing Agent and Buffer test projects on a .NET 10-capable host.
4. Add CI or documented local commands that make the required SDK and Windows-only target expectations explicit.

## Profile and fixture coverage

5. Add golden raw-input to NDJSON-output fixtures for syslog, CSV, auditd, Windows Event Log, and ETW/ETL where host-neutral fixtures are possible.
6. Prove whether nested field access works in profile KQL:
   - `EventData.TargetUserSid`,
   - `SYSCALL.SYSCALL`,
   - `EXECVE.ARGV`,
   - `_metadata.profileId`.
7. Decide whether KQL row exposure must be flattened for nested fields.
8. Harden profile validation around metadata preservation and malformed profile diagnostics.

## Input hardening

9. Harden auditd assembler completion rules and malformed-record handling.
10. Extend auditd decoding toward LAUREL-level behavior without adding server-side normalization to the edge.
11. Improve Windows Event Log field extraction documentation with examples for common Security, Sysmon, PowerShell, SMB, and Defender profiles.

## Forwarder hardening

12. Exercise the existing buffered RELP forwarder path against the separate demo collector for success, transient failure, retry, permanent failure, dead-letter, and restart recovery.
13. Add daemon-facing examples for the `forwarder` sink and `dzdemo-collector` validation executable.
14. Wire the buffered-forwarder health snapshot into the Agent diagnostic output surface.
15. Preserve delivery identity and metadata outside user-controlled KQL projections.
16. Continue hardening the RELP.Net adapter behind `IForwarderTransport`.
17. Add TLS, certificate validation, reconnect/backoff, endpoint failover, and production receiver documentation after plain RELP works.

## Architecture discipline

18. Keep `DeltaZulu.Buffer` as the authoritative durability and backpressure layer.
19. Keep RELP.Net details behind the forwarder transport adapter.
20. Defer broad Clean/Onion project restructuring until the RELP adapter and daemon host prove which boundaries need extraction.
