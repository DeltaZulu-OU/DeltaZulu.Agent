# DeltaZulu.Agent — documentation

Architecture, Decisions, Constraints and roadmaps for the DeltaZulu estate live
in **[`DeltaZulu-OU/docs`](https://github.com/DeltaZulu-OU/docs)**, not here.

| Looking for | Go to |
|---|---|
| Decisions governing this repository | [`architecture/GOVERNING-DECISIONS.md`](https://github.com/DeltaZulu-OU/docs/blob/main/architecture/GOVERNING-DECISIONS.md) |
| The estate-wide pipeline architecture | [`architecture/PIPELINE.md`](https://github.com/DeltaZulu-OU/docs/blob/main/architecture/PIPELINE.md) — read with `PIPELINE-ERRATA.md` |
| Facts the estate does not control | [`constraints/`](https://github.com/DeltaZulu-OU/docs/tree/main/constraints) |
| This repository's historical ADRs | [`archive/DeltaZulu.Agent/`](https://github.com/DeltaZulu-OU/docs/tree/main/archive/DeltaZulu.Agent) |
| Roadmaps | [`roadmaps/`](https://github.com/DeltaZulu-OU/docs/tree/main/roadmaps) |
| Verification evidence | [`reports/`](https://github.com/DeltaZulu-OU/docs/tree/main/reports) |

Decisions are numbered globally across the estate. The per-repository scheme this
replaced produced collisions that citations could not resolve — `DeltaZulu.Agent`
ADR 0014 and `DeltaZulu.Platform` ADR 0014 decide opposite things, and the Agent
carried two different ADR 0003 documents, so "ADR 0003" did not resolve even
within one repository.

## What remains here

The Agent's own architecture and analysis documents stay for now:
`ARCHITECTURE.md`, `ENDPOINT_SCHEMA_EXPECTATIONS.md`, `ETW_SCHEMA_BOUNDARIES.md`,
`FORWARD_PROTOCOL_SPECIFICATION.md`, `KQL_SUPPORT_BOUNDARY.md` and the
RealtimeKQL comparisons among them.

`ROADMAP.md` and `DZAGENTCTL_CONTROLLER_ROADMAP.md` have moved to the docs
repository and gained review triggers.

Two amendments recorded against the archived Agent ADRs, which the archive itself
is frozen against carrying:

- **ADR 0016's bespoke-native-sink premise is retired** by `DEC-0017`. It
  inherited a factual error from ADR 0012, which rejected Proton's REST ingest
  API as an Enterprise-only feature; `ProtonHttpExecutor` ships in Platform today.
- **ADR 0003 is ambiguous by construction** — two different documents carry that
  number.
