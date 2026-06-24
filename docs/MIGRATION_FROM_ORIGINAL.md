# Migration from the original library

This implementation is a restructuring, not a greenfield rewrite. The original design mixed input, KQL processing, output, and lifecycle behavior around `EventComponent`. The new structure separates those responsibilities.

## Main replacements

| Original | New |
|---|---|
| `RealTimeKqlLibrary` namespace | `DeltaZulu.Agent.*` namespaces |
| `EventComponent` | `IResourceInput` + `ResourcePipeline` + profile executor |
| `IOutput` | `IResourceSink` |
| `Observable<T>` | `System.Reactive` primitives |
| `ModifierSubject<T>` | Rx `Select`, `Subject<T>`, or `Observable.Create` |
| Query file array in constructor | YAML resource profile with embedded KQL |
| JSON array file output | NDJSON envelope output |
| ADX/Blob direct output | intentionally not carried into the first restructuring; emit NDJSON and let the daemon/server forward |
| `Microsoft.Syslog` project reference | lightweight parser behind `DeltaZulu.Agent.Inputs.Syslog`; can later be replaced by a parser adapter |

## Preserved source capability

The Windows input project carries forward the original ETW/ETL/EVTX/Event Log concept using `Tx.Windows` and `Tx.Windows.Logs`.

## Breaking API policy

Breaking API changes are intentional. The library now exposes host-neutral components instead of CLI-style event components.
