# Roadmap

DeltaZulu.Agent has moved past the initial library-and-buffer spike. The current priority is to harden the forwarder path that now connects filtered resource records to `DeltaZulu.Buffer` and the demo ACK transport, then replace the demo transport with production RELP/TLS.

## P0: Validate and stabilize the current implementation

- Restore, build, and test the solution on a host with the .NET 10 SDK.
- Fix any `Microsoft.Rx.Kql`, `Tx.Windows`, or target-framework compatibility issues exposed by the first clean build.
- Keep the existing fast unit-test posture and add missing golden fixture tests for raw input to NDJSON output.
- Prove nested field access in profile KQL for Windows, auditd, and metadata fields before deciding whether KQL row exposure must be flattened.
- Harden auditd assembler completion rules, malformed-record handling, and LAUREL-style decoding behavior.

## P0: Harden the buffered forwarder demo

- Keep `DeltaZulu.Buffer` wired after KQL filtering and before network delivery.
- Exercise the demo forwarder server/client with successful send, retry, permanent failure, dead-letter, and restart-recovery scenarios.
- Add operator-facing examples for forwarding to the demo ACK server, including recommended buffer directories and cleanup expectations.
- Add buffer and forwarder health metrics to the Agent diagnostic output: written, sent, acknowledged, retried, dead-lettered, rejected, and oldest buffered age.
- Preserve agent/source/profile delivery metadata outside user-controlled KQL projections so forwarding identity cannot be accidentally dropped.

## P1: Production RELP transport

- Implement a RELP.Net-backed transport adapter behind the existing RELP-neutral `IForwarderTransport` port.
- Treat RELP acknowledgements as transport results while keeping durable commit/delete decisions in the buffer/application side.
- Add reconnect, endpoint selection, jittered backoff, and transient/permanent failure classification.
- Add TLS configuration, client certificate validation, and certificate-expiry diagnostics after plain local RELP works.
- Document rsyslog/syslog-ng receiver snippets once the RELP/TLS path is validated.

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
