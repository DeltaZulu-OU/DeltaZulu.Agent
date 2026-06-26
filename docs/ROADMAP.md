# Roadmap

DeltaZulu.Agent has moved past the initial library-and-buffer spike. The current priority is to harden the forwarder path that connects filtered resource records to `DeltaZulu.Buffer` and RELP.Net transport, with a separate demo collector available only for local validation.

## P0: Validate and stabilize the current implementation

- Restore, build, and test the solution on a host with the .NET 10 SDK.
- Fix any `Microsoft.Rx.Kql`, `Tx.Windows`, or target-framework compatibility issues exposed by the first clean build.
- Keep the existing fast unit-test posture and add missing golden fixture tests for raw input to NDJSON output.
- Prove nested field access in profile KQL for Windows, auditd, and metadata fields before deciding whether KQL row exposure must be flattened.
- Harden auditd assembler completion rules, malformed-record handling, and LAUREL-style decoding behavior.

## P0: Harden the buffered RELP forwarder

- Keep `DeltaZulu.Buffer` wired after KQL filtering and before network delivery.
- Exercise the RELP forwarder against the separate demo collector with successful send, retry, permanent failure, dead-letter, and restart-recovery scenarios.
- Add operator-facing examples for forwarding to the demo collector, including recommended buffer directories and cleanup expectations.
- Add buffer and forwarder health metrics to the Agent diagnostic output: written, sent, acknowledged, retried, dead-lettered, rejected, and oldest buffered age.
- Preserve agent/source/profile delivery metadata outside user-controlled KQL projections so forwarding identity cannot be accidentally dropped.

## P1: Production RELP transport

- Continue hardening the RELP.Net-backed transport adapter behind the existing RELP-neutral `IForwarderTransport` port.
- Treat RELP acknowledgements as transport results while keeping durable commit/delete decisions in the buffer/application side.
- Keep forwarder transport settings YAML-driven, then add reconnect, endpoint selection refinements, jittered backoff, and transient/permanent failure classification.
- Finish RELP/TLS hardening by wiring the configured certificate policy into RELP.Net server-certificate validation callbacks, adding certificate-expiry diagnostics, and documenting receiver setup.
- Keep rsyslog/syslog-ng receiver snippets in `docs/RELP_RECEIVER_SETUP.md` aligned with validated plain RELP/TLS behavior.

## P1: Delivery correctness and operations

- Add stable delivery IDs and at-least-once deduplication fields for safe resend after crashes or network failures.
- Tie future source checkpoint advancement to durable enqueue rather than network ACK.
- Add profile hot reload once forwarding and checkpoint semantics are clear.
- Add typed resource-local enrichment providers where they can run without changing server-canonical normalization.
- Add SID/account resolution, auditd process relationship state, Windows LogonId/session state, and Sysmon ProcessGuid state as optional local enrichments.

## P2: Host and structure hardening

- Add a daemon/service host, lifecycle integration, installers, and signed configuration after the library path is stable.
- Add journald input.
- Introduce `DeltaZulu.Agent.Application`, `DeltaZulu.Agent.Transport.Relp`, or `DeltaZulu.Agent.Domain` only when code pressure proves those boundaries are needed.
- Keep input adapters stable and avoid architecture-only project reshuffles.

## Permanently out of scope

- DuckDB.
- SQL window engine.
- Edge-side server-canonical normalization.
- A built-in syslog daemon replacement.