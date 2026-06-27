# RealTimeKql influences

This implementation is inspired by RealTimeKql, but it is not a restructuring of that codebase. RealTimeKql mixed input, KQL processing, output, and lifecycle behavior around `EventComponent`; DeltaZulu.Agent uses separate host-neutral components for those responsibilities.

## Conceptual mapping

| RealTimeKql concept | DeltaZulu.Agent concept |
|---|---|
| `RealTimeKqlLibrary` namespace | `DeltaZulu.Agent.*` namespaces |
| `EventComponent` | `IResourceInput` + `ResourcePipeline` + profile executor |
| `IOutput` | `IResourceSink` |
| `Observable<T>` | `System.Reactive` primitives |
| `ModifierSubject<T>` | Rx `Select`, `Subject<T>`, or `Observable.Create` |
| Query file array in constructor | YAML resource profile with embedded KQL |
| JSON array file output | NDJSON envelope output |
| ADX/Blob direct output | intentionally not included in this agent; emit NDJSON and let the daemon/server forward |
| `Microsoft.Syslog` project reference | lightweight parser behind `DeltaZulu.Agent.Inputs/Syslog`; can later be replaced by a parser adapter |

## Inspired source capability

The Windows input project follows the same ETW/ETL/EVTX/Event Log problem space using `Tx.Windows` and `Tx.Windows.Logs`.

## Breaking API policy

Breaking API changes are intentional. The library now exposes host-neutral components instead of CLI-style event components.
