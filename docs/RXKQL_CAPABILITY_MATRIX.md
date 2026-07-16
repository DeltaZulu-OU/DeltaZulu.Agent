# Rx.Kql capability matrix

This matrix is the executable compatibility contract for DeltaZulu.Agent's
pinned [`Microsoft.Rx.Kql` 3.5.3](../Directory.Packages.props) dependency.
Each result is exercised by `RxKqlCapabilityTests`; it is not a hand-maintained
Agent operator policy. Re-run that suite whenever the package, a shim, source
binding, or workbench execution path changes.

The unit suite also starts every checked-in profile against an empty observable.
That verifies its query parses with the pinned package without turning YAML
validation into a second KQL operator policy.

| Feature | Classification | Executable evidence / runtime boundary |
| --- | --- | --- |
| Source/table selection | RxKqlSupported | `Source` executes; the Agent rewrites the declared profile table to the one Rx.Kql observable named `Source`. |
| `where` and scalar expressions | RxKqlSupported | A compound equality and `has` predicate executes over a live row. |
| `extend` | RxKqlSupported | A calculated field (`Value * 2`) executes and is emitted. |
| `project` and named projection | RxKqlSupported | Multi-column projection and `Renamed = Value` projection both execute. |
| Aggregation/windowing | Unsupported by the pinned engine | `summarize Total = count()` fails during Rx.Kql parsing/execution in 3.5.3. No streaming aggregation/windowing contract is advertised. |
| `union` | Unsupported by the pinned engine | The 3.5.3 observable parser reports that `union` is not implemented. The current Agent also supplies one observable binding only. |
| `join` | Unsupported by the pinned engine | The 3.5.3 observable parser reports that `join` is not implemented. A future multi-stream runtime must still define bindings, completion, and bounded state before making joins usable. |
| Table alias rewrite | AgentCompatible | The profile input table is normalized to the `Source` observable before Rx.Kql parses the query. |
| `notin` | AgentCompatible | The Agent normalizes the Kusto alias to Rx.Kql's `!in`. |
| `has_any` | AgentCompatible | The Agent expands a value list to a parenthesized sequence of `has` predicates. |
| Agent scalar helpers | AgentCompatible | `isnotempty`, `strcat_array`, `ntohs`, and `getprocessname` are registered before query execution. |

## Multi-stream runtime availability

The feature classification above answers what the pinned engine can execute;
it does not itself grant an Agent query access to additional tables. The current
profile executor supplies exactly one observable (`Source`) and rewrites the
profile's declared table to that binding. Therefore no named secondary source,
schema/table resolver, completion coordination, or bounded state contract is
available for a practical multi-stream query.

For version 3.5.3, `union` and `join` are already **Unsupported by the pinned
engine**, so no Agent runtime limitation is being used to reject them. If a
future pinned engine supports either operator, its matrix row must separately
state **RxKqlSupported, Agent runtime unavailable** until named bindings,
lifecycle/completion semantics, and bounded-memory behavior are implemented
and covered by integration tests. This distinction prevents parser recognition
from being advertised as usable Agent multi-stream execution.

## Release and regression checklist

When updating `Microsoft.Rx.Kql`, update its pinned version in
`Directory.Packages.props` and this matrix in the same change, then run the
capability tests and the checked-in profile startup test. The CI workflow is
also triggered by package, profile, source/executor, workbench, and matrix
changes. New privacy, compliance, bandwidth, or delivery restrictions require
their own product requirement and ADR; they must not be represented as an
undocumented KQL syntax restriction.

## How to interpret the classifications

- **RxKqlSupported** means the test proves that the pinned engine parses and
  executes the stated feature over an Agent live observable.
- **AgentCompatible** means the same execution additionally depends on an
  Agent shim or registered helper.
- **Unsupported by the pinned engine** means the query is rejected because
  Rx.Kql 3.5.3 cannot parse/execute it. It is not a separate Agent syntax
  restriction.

Diagnostics are emitted by the shared `ResourceKqlProfileExecutor` as a
`KqlQueryExecutionException`. They identify the profile and whether failure
occurred during Agent shim normalization, Rx.Kql parsing, or Rx.Kql execution.
Original and normalized query text are structured exception properties rather
than message text, allowing each host to make a safe diagnostic-display choice.

For authoring and provenance guidance, see
[the KQL support boundary](KQL_SUPPORT_BOUNDARY.md) and
[ADR 0004](adr/0004-kql-capability-alignment.md).
