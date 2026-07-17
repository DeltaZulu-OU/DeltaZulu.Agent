# RPC-centric Agent ↔ Platform alignment

DeltaZulu's RPC detection architecture keeps the endpoint agent thin and deterministic:

```text
DeltaZulu.Agent emits enriched facts.
DeltaZulu.Platform emits detections.
```

The agent may resolve protocol facts such as `MS-SCMR` opnum `12` to
`RCreateServiceW` with `OperationCategory = ServiceCreate`. It must not emit
verdicts such as `RemoteServiceCreation`, `DCSync`, `LateralMovement`,
suppression decisions, tenant policy decisions, or severity.

## Agent-owned contract

Agent-owned RPC work is limited to endpoint-local evidence. The current, transitional profile-centric runtime follows Logstash-style ordering (`input` → `filter` → deterministic enrichment → `output`): `ResourcePipeline` applies RPC enrichment after profile filtering and before the output writer (`ResourceOutputEnricher.EnrichAfterFilter` in `AgentRuntime`) so filters continue to evaluate source-native fields while outputs carry deterministic sidecar facts:

- preserve raw ETW/EventLog fields and raw payloads for replay;
- emit delivery and parser metadata (`profileId`, `profileVersion`, `eventUid`,
  parser/resolver versions, source identity, host identity);
- run deterministic RPC UUID/opnum resolution for the versioned P0 resolver map;
- emit the sidecar `enrichment.Rpc` object when RPC identity fields are present;
- classify obvious local versus remote RPC without marking missing-network-address
  records remote;
- keep high-volume RPC and Security collection behind explicit profiles.

The first P0 resolver version is `rpc-map-2026.07.1`. It covers MS-SCMR / svcctl
and MS-DRSR / DRSUAPI only. Unknown UUID/opnum pairs are preserved as raw event
fields and must remain unresolved rather than receiving guessed names.

### Open question: enrichment ordering in the target architecture

[`ARCHITECTURE.md`](ARCHITECTURE.md) lists `Enrichment/` (ETW, RPC, and Windows
enrichment) as one of `DeltaZulu.Pipeline`'s assembly boundaries, but its
runtime topology diagram and "Dispatch, commits, and observability" section do
not state where deterministic enrichment runs relative to the coordinated
filter dispatcher and the `agent.output` commit. The current
filter-then-enrich-then-output ordering described above is a property of the
transitional `ResourcePipeline`, not a target-architecture decision recorded in
an ADR. Reconcile this — most likely as a clarification to ADR 0008 or a new
ADR — before or during [`ROADMAP.md`](ROADMAP.md) Phase 12 (coordinated filter
dispatch), since the dispatcher's per-event output list is exactly where an
enrichment step would need to run if it stays positioned after filtering.

## Wire shape

RPC-capable agent output preserves native/projected event fields separately from
agent-side deterministic enrichment:

```json
{
  "_metadata": {
    "profileId": "windows.etw.rpc.p0",
    "profileVersion": "1.1.0",
    "sourceType": "WindowsEtw",
    "sourceName": "Microsoft-Windows-RPC",
    "eventUid": "7f6a20e8a9c24d7eb63af96a3a0f1a1c",
    "resolverVersion": "rpc-map-2026.07.1",
    "rawPreserved": true
  },
  "event": {
    "ProviderName": "Microsoft-Windows-RPC",
    "EventId": 5,
    "InterfaceUuid": "367abb81-9844-35f1-ad32-98f038001003",
    "ProcNum": 12,
    "Endpoint": "49679",
    "NetworkAddress": "192.168.10.25"
  },
  "enrichment": {
    "Rpc": {
      "InterfaceUuid": "367abb81-9844-35f1-ad32-98f038001003",
      "InterfaceName": "MS-SCMR",
      "ProcNum": 12,
      "OperationName": "RCreateServiceW",
      "OperationCategory": "ServiceCreate",
      "Endpoint": "49679",
      "NetworkAddress": "192.168.10.25",
      "IsLocal": false,
      "IsRemote": true,
      "ResolverVersion": "rpc-map-2026.07.1"
    }
  }
}
```

Platform Bronze must preserve `event` and `enrichment` unchanged. Platform Silver
must promote `enrichment.Rpc` into canonical `silver.RpcEvent` columns so
detection authors query canonical fields instead of raw provider-specific field
names.

## Profile contract

- `profiles/windows/etw/rpc-p0.yaml` is a selective production profile for the
  P0 SCMR and DRSR interfaces/endpoints. It must not keep every event with a
  missing interface UUID. A future full-fidelity profile should use a distinct ID
  such as `windows.etw.rpc.full` or `windows.etw.rpc.debug`.
- `profiles/windows/eventlog/security.yaml` remains the baseline Security profile
  and continues suppressing global `5156` volume.
- `profiles/windows/eventlog/security-rpc-correlation.yaml` is disabled by
  default and retains `4624`, `4662`, and targeted `5156` records for RPC
  correlation. Windows Event Log parsing normalizes `5156` application and
  destination-port aliases to canonical `ApplicationPath` and `DestinationPort`
  when possible, while the profile keeps alias fallbacks until canonical parser
  coverage is proven in fixtures.

## Platform-owned work

The platform owns Bronze storage, Silver/Golden modeling, CMDB and identity joins,
correlation, suppression, scoring, alerting, deduplication, evidence bundles, and
replay. The agent repository must not add platform detection rules, alert
materialization, CMDB joins, identity allowlists, or severity policy.

## Known follow-up gaps

- `ResolverVersion` is still a common metadata field; a future schema should use
  namespaced resolver versions such as `resolverVersions.rpc`.
- RPC locality is currently binary (`IsLocal` / `IsRemote`); Silver should later
  distinguish `Local`, `Remote`, and `Unknown`.
- Process key, process snapshot, network tuple, service evidence, and
  fixture-driven cross-project compatibility tests remain required follow-up
  work before production rollout.
- Enrichment ordering relative to the coordinated filter dispatcher is not yet
  decided in the target architecture; see "Open question: enrichment ordering
  in the target architecture" above.
