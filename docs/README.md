# DeltaZulu.Agent — documentation

Estate-wide architectural policy, Constraints, the current target and planning are maintained in [DeltaZulu-OU/docs](https://github.com/DeltaZulu-OU/docs). Local documents describe source, interfaces and historical reasoning; they do not override Accepted Decisions.

| Looking for | Go to |
|---|---|
| Current target | [NRT pipeline](https://github.com/DeltaZulu-OU/docs/blob/master/architecture/NRT-PIPELINE.md) |
| Governing Decisions | [Decision index](https://github.com/DeltaZulu-OU/docs/blob/master/architecture/GOVERNING-DECISIONS.md) |
| Constraints and qualifications | [Constraints](https://github.com/DeltaZulu-OU/docs/tree/master/constraints) |
| Active work | [Consolidated roadmap](https://github.com/DeltaZulu-OU/docs/blob/master/roadmaps/CONSISTENCY-CORRECTNESS-ROADMAP.md) (C05, C10, C12, C13, C17) |
| Historical architecture | [Pipeline import](https://github.com/DeltaZulu-OU/docs/blob/master/architecture/PIPELINE.md) with [errata](https://github.com/DeltaZulu-OU/docs/blob/master/architecture/PIPELINE-ERRATA.md) |
| Historical ADRs | [Archive](https://github.com/DeltaZulu-OU/docs/tree/master/archive/DeltaZulu.Agent) |
| Dated verification evidence | [Reports](https://github.com/DeltaZulu-OU/docs/tree/master/reports) |

## Local scope

Local implementation references remain in this directory, including `ARCHITECTURE.md`, the Forward protocol, endpoint schema expectations and KQL/ETW comparison notes. Older Proton sink, collector and pipeline plans are historical where they conflict with DEC-0033. Do not infer implementation of the LocalStream pipeline from the central target. The controller plan and migration milestones now live in the consolidated table.

Only tested behavior is verified; a target architecture or historical report is not proof of current runtime conformance.
