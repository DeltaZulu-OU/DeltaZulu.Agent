# RealTimeKql ETW comparison

This note tracks the ETW behavior that DeltaZulu intentionally keeps close to
[`microsoft/KqlTools/Source/RealTimeKqlLibrary`](https://github.com/microsoft/KqlTools/tree/master/Source/RealTimeKqlLibrary) and the places where DeltaZulu
still differs.

## Behavior kept aligned

| RealTimeKql behavior | DeltaZulu behavior |
|---|---|
| `EtwSession` requires Administrator before attaching to real-time ETW. | `EtwSessionInput.Open` checks the current principal before opening the ETW input. |
| `EtwSession` attaches to an existing ETW session with `Tx.Windows.EtwTdhObservable.FromSession(sessionName)`. | `EtwSessionInput` remains attach-only and uses `Tx.Windows.EtwTdhObservable.FromSession(_sessionName)`. |
| `EtwSession` names the KQL stream as `etw` + session name. | `EtwSessionInput.Name` and source metadata default to `etw{sessionName}`. |
| `EtlFileReader` reads ETL files with `Tx.Windows.EtwTdhObservable.FromFiles(fileName)`. | `EtlFileInput` reads ETL files with `Tx.Windows.EtwTdhObservable.FromFiles(_path)`. |

## Deliberate differences

| Difference | Why it exists |
|---|---|
| DeltaZulu wraps native events in `SourceEvent` metadata before KQL. | The pipeline must preserve profile id, source type/name, platform, host, and delivery metadata across filtering and forwarding. |
| DeltaZulu has an additional managed ETW mode. | Attach mode stays RealTimeKql-compatible; managed mode is a DeltaZulu extension that owns session creation, provider enablement, and shutdown. |

## RealTimeKql command-line flow

The RealTimeKql CLI documentation demonstrates ETW as a three-step operator
workflow:

1. Start a real-time ETW trace session externally, for example
   `logman.exe create trace tcp -rt -nb 2 2 -bs 1024 -p <provider-guid> <keywords> -ets`.
2. Attach RealTimeKql to that session by name, for example
   `RealTimeKql etw tcp json`.
3. Stop the trace session externally when finished, for example
   `logman.exe stop tcp -ets`.

DeltaZulu follows the same ownership split for ETW profiles: the agent attaches
to `resource.session`; session creation, provider enablement, keyword selection,
buffer sizing, and session shutdown remain operator/controller responsibilities
outside the ETW input adapter.

## Sessions versus providers

RealTimeKql is a deterministic subscriber: the ETW session must already exist.
DeltaZulu keeps that behavior in `resource.mode: attach`. For deployments that
do not create sessions externally with `logman`, `resource.mode: managed` is the
explicit DeltaZulu extension: the agent creates a named `TraceEventSession`,
enables `resource.provider`, subscribes to it, and stops only that owned session
when the subscription is disposed. In other words,
`logman query -ets` answers whether the session exists; it does not list every
provider enabled in a session.

For DeltaZulu attach profiles, `resource.session` is the only ETW control input
and `resource.provider` is identity/filtering metadata for KQL. For managed
profiles, `resource.session` names the DeltaZulu-owned session and
`resource.provider` is the provider the agent enables. If ETW session tooling cannot find `Microsoft-Windows-Kernel-Process`
as a session, that is expected unless someone created a trace session with that
exact name; it does not prove the provider is unavailable.

## Built-in ETW profile availability

The built-in kernel-process ETW profile uses managed mode because DeltaZulu owns
that session lifecycle instead of relying on a manually-created `logman` trace.
Its `resource.session` names the DeltaZulu-owned session. Do not point this
profile at arbitrary system sessions such as `Circular Kernel Context Logger`
just because they appear in `logman query -ets`; some sessions are not consumable
through the Tx.Windows real-time attach path and may fail with WMI provider
instance-name errors.

## Missing session error handling

If an attach-mode ETW session does not exist, DeltaZulu should fail the input
open with an actionable error. Managed-mode profiles are different: the agent
creates and owns the configured session, so missing-session handling is part of
the managed input lifecycle.

## Schema decision

DeltaZulu should not define a fixed query schema for ETW input at the edge. ETW
has a reliable header/envelope, but provider payloads are event/version-specific.
For profile KQL, keep the input native: query the fields emitted by Tx.Windows for
the selected session/provider, and preserve provider-specific fields without
mapping them into Windows Event Log aliases.

A conservative Bronze ETW envelope is still useful as a downstream storage
contract, but it should be introduced separately from the live input adapter. The
input adapter's job remains RealTimeKql-like collection plus DeltaZulu metadata
and guardrails; Bronze/Silver/Golden normalization belongs in later server-side
or explicitly mapped processing where `ProviderId + EventId + EventVersion` can
select a known parser.

## Troubleshooting implication

When ETW troubleshooting diverges from RealTimeKql, start with the attach-only
subset:

1. Verify `resource.session` appears in `logman query -ets`.
2. Verify the process is elevated.
3. Filter providers with native ETW fields such as `ProviderName`; do not rely on `source` to mean provider.
4. Use fields observed from the selected session/provider; avoid assuming a universal ETW payload schema.

This keeps the first diagnostic step equivalent to `RealTimeKql etw <session>
json` before DeltaZulu-specific profile filtering is evaluated.
