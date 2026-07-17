# DeltaZulu Agent and Platform Responsibilities for Collection Coverage Evaluation

## Purpose

DeltaZulu needs a clear separation of responsibilities between the Agent and the Platform for collection coverage evaluation.

The evaluation model follows the telemetry path:

```text
event happens
  -> source generates a log if generation policy enables it
  -> agent reads the source
  -> agent filters local noise
  -> agent forwards records
  -> platform stores and normalizes records
  -> SIEM/detection rule evaluates records
  -> alert is created if rule conditions match
```

The goal is to measure both missing useful telemetry and excessive low-value telemetry.

Two cost classes must be represented:

| Cost | Meaning |
| --- | --- |
| Opportunity cost | A useful or expected log was not generated, read, kept, forwarded, stored, or made available to a dependent rule. |
| Noise cost | A log was generated, read, kept, forwarded, or stored despite having no declared current purpose, excessive volume, or low analytical value. |

This document defines which parts belong to the Agent and which parts belong to the Platform.

## Core Principle

```text
The Agent emits local facts.
The Platform owns stateful evaluation.
CMDB stores host-local context.
Silver normalization resolves deterministic field values.
Golden exposes purpose-shaped analytical views.
```

The Agent must not become the authoritative coverage engine. It should provide bounded, local, time-windowed facts.

The Platform must compute final coverage, opportunity cost, noise ratio, rule usability, alert correlation, and recommendations.

## Definitions

### Agent

The Agent is the endpoint-side collection and forwarding component.

It observes local source state, reads telemetry, applies local filtering, tracks pipeline counts, and forwards records or observations to the Platform.

### Platform

The Platform is the central evaluation, storage, normalization, detection, and analytics system.

It stores raw and normalized telemetry, maintains rule and profile metadata, joins CMDB context, computes metrics, evaluates coverage, and generates recommendations.

### CMDB

CMDB is the namespace for host-local and environment-local state.

It contains inventory and configuration tables that can be joined with telemetry and observations. CMDB data is not copied into every event row as inline enrichment.

### Lookup Resolution

Lookup resolution is deterministic static field-value decoding.

Examples:

```text
LogonType 10 -> RemoteInteractive
Status 0xC000006A -> STATUS_WRONG_PASSWORD
S-1-5-18 -> LocalSystem
TicketEncryptionType 0x17 -> RC4_HMAC
```

Lookup resolution belongs in Silver normalization and produces `<FieldName>_resolved` sibling fields.

### Coverage Evaluation

Coverage evaluation is the Platform-side process of determining whether required telemetry exists at each boundary of the chain:

```text
generation -> read -> filter -> forward -> store -> normalize -> rule evaluate -> alert
```

## Responsibility Boundary

| Capability | Agent | Platform |
| --- | ---: | ---: |
| Read Windows Event Log / ETW sources | Yes | No |
| Apply local source filters | Yes | No |
| Emit raw source records | Yes | Stores |
| Emit source health observations | Yes | Stores and evaluates |
| Emit pipeline counts | Yes | Stores and evaluates |
| Emit filter summaries | Yes | Stores and evaluates |
| Emit forwarding health | Yes | Stores and evaluates |
| Read local audit policy state | Yes, after CMDB implementation | Stores in CMDB |
| Read local provider/channel state | Yes, after CMDB implementation | Stores in CMDB |
| Maintain CMDB namespace | No | Yes |
| Maintain rule dependency metadata | No | Yes |
| Maintain lookup catalogs | No | Yes |
| Add `_resolved` fields | No | Yes, in Silver |
| Compute opportunity cost | No | Yes |
| Compute noise ratio | No | Yes |
| Correlate SIEM alerts | No | Yes |
| Compute rule usability | No | Yes |
| Generate recommendations | No | Yes |
| Store historical baselines | No | Yes |

## Agent Responsibilities

The Agent is responsible for endpoint-local observation and delivery.

### 1. Source Collection

The Agent reads configured local sources.

Examples:

```text
Windows Event Log
ETW providers
Sysmon channels
PowerShell logs
DNS client logs
Security log
Application/System logs
```

The Agent should extract source-native fields faithfully.

The Agent should preserve:

```text
SourceType
Channel
ProviderName
ProviderGuid
EventId
Version
RecordId
Timestamp
Computer
EventData/UserData fields
Raw payload where configured
```

The Agent should not reinterpret Windows field meanings beyond structural parsing.

### 2. Local Filtering

The Agent applies local filters to reduce noise before forwarding.

The Agent may filter on raw values:

```text
EventId == 4688
LogonType == 10
Status == 0xC000006A
ProviderName == Microsoft-Windows-Security-Auditing
```

The Agent should not require semantic lookup catalogs to filter on values like:

```text
LogonType_resolved == RemoteInteractive
Status_resolved == STATUS_WRONG_PASSWORD
```

Those semantic expressions should be compiled or authored into raw-value filters before deployment to the Agent.

### 3. Local Observability

The Agent emits operational facts about its local collection pipeline.

Required observation families:

```text
collector.source.health
collector.pipeline.counts
collector.filter.summary
collector.forwarder.health
```

Later, after CMDB implementation:

```text
collector.audit_policy.state
collector.event_channel.state
collector.event_provider.state
```

### 4. Local Coverage State, Not Final Analysis

The Agent may emit bounded local coverage state, but it must remain factual.

Recommended record kind:

```text
collector.coverage.local_state
```

This is not a final coverage evaluation. It is local evidence for Platform-side evaluation.

Allowed fields:

| Field | Meaning |
| --- | --- |
| `cmdbEntityId` | CMDB host/service/workload entity. |
| `evaluationId` | Local evaluation window id. |
| `sourceType` | Source family. |
| `channel` | Event channel. |
| `provider` | Provider name or GUID. |
| `eventId` | Event ID when available. |
| `profileId` | Applied collection profile, carried in observation metadata. |
| `filterId` | Applied local filter, carried in observation metadata. |
| `sourceExists` | Source exists locally. |
| `sourceReadable` | Agent can read source. |
| `readErrorCount` | Local read failures. |
| `actualReadCount` | Records read by agent. |
| `keptAfterFilterCount` | Records kept after local filter. |
| `discardedCount` | Records discarded locally. |
| `outputAcceptedCount` | Records accepted by local output/buffer. |
| `outputFailedCount` | Output failures. |
| `windowStart` | Observation window start, carried in observation metadata. |
| `windowEnd` | Observation window end, carried in observation metadata. |

This local-state model predates [`docs/adr/0009-unrecognized-events-and-blindness-measurement.md`](adr/0009-unrecognized-events-and-blindness-measurement.md), which is the target architecture's authoritative definition of collection coverage measurement and uses a more precise vocabulary than the single `discardedCount` bucket above. ADR 0009 distinguishes admission rejection, parser no-match (preserved as `Unrecognized`, not discarded), filter `NoCandidate` versus `NoMatch`, and operational failures (`FilterError`/`OutputError`, which are not blindness); "complete blindness" is specifically an unrecognized plaintext event with zero filter outputs. When this document's local-state fields are next revised, reconcile `discardedCount` and the opportunity-cost gap table below (`Filter gap`, `Forwarding gap`) against that disposition set rather than treating "kept vs. discarded" as binary — a record ADR 0009 preserves as `Unrecognized` is not the same opportunity-cost signal as one that fails a filter with candidates, or one that fails for an operational reason.

After CMDB implementation, local Windows audit state may also be reported:

| Field | Meaning |
| --- | --- |
| `auditPolicyCategory` | Windows audit policy category. |
| `auditPolicySubcategory` | Windows audit policy subcategory. |
| `auditPolicySubcategoryGuid` | Stable subcategory GUID. |
| `auditSuccessEnabled` | Success auditing enabled. |
| `auditFailureEnabled` | Failure auditing enabled. |

The Agent must not emit final evaluation claims.

Disallowed agent-side fields:

```text
expectedCollectCount
expectedForwardCount
unexpectedCollectCount
noiseRatio
opportunityCostCount
ruleId
siemAlertId
final analysis
recommendation
```

These require central state.

### 5. Forwarding

The Agent forwards raw records and local observations to the Platform.

Forwarding observations should distinguish between:

| Counter | Meaning |
| --- | --- |
| `keptAfterFilterCount` | Records that survived local filtering. |
| `outputAcceptedCount` | Records accepted by local output/buffer. |
| `outputFailedCount` | Records rejected or failed before output acceptance. |
| `deliveredCount` | Records acknowledged by central receiver, if available later. |

`forwardedCount` should not be used ambiguously. If the record is only accepted by a local durable buffer, call it `outputAcceptedCount`.

## Platform Responsibilities

The Platform owns stateful interpretation and evaluation.

### 1. Bronze Storage

Bronze stores raw received records and observations.

Bronze should preserve:

```text
raw event payload
raw EventData fields
agent metadata
host metadata
ingest metadata
recordKind
source envelope
```

Bronze does not apply semantic lookup resolution.

### 2. Silver Normalization

Silver applies deterministic normalization.

Silver adds `_resolved` fields for static lookup values.

Examples:

```json
{
  "Status": "0xC000006A",
  "Status_resolved": "STATUS_WRONG_PASSWORD"
}
```

```json
{
  "LogonType": "10",
  "LogonType_resolved": "RemoteInteractive"
}
```

```json
{
  "SubjectUserSid": "S-1-5-18",
  "SubjectUserSid_resolved": "LocalSystem"
}
```

Silver must preserve original fields.

Rule:

```text
Never overwrite the original Windows value.
Always add a sibling resolved field.
```

### 3. Lookup Catalog Ownership

The Platform owns lookup catalogs.

Lookup sources may include:

```text
Microsoft Learn
Microsoft Open Specifications
Azure Sentinel ASIM parsers
Elastic Beats / Elastic Integrations
Splunk lookup content
Eric Zimmerman evtx maps
ETW provider manifests
```

Lookup catalogs should support:

```text
global lookups
contextual lookups
bitmask lookups
provider-specific lookups
version-specific lookups
```

Lookup key shape:

```text
SourceType
Channel
ProviderName or ProviderGuid
EventId
Version
FieldName
RawValue
```

### 4. CMDB Namespace

The Platform owns the CMDB namespace.

CMDB tables provide joinable host-local context.

Initial namespace:

```text
Cmdb
```

Candidate tables:

| Table | Purpose |
| --- | --- |
| `Cmdb.Host` | Host identity, OS, domain/workgroup, agent identity. |
| `Cmdb.EventChannel` | Available Windows Event Log channels. |
| `Cmdb.EventProvider` | Installed Event Log / ETW providers. |
| `Cmdb.AuditPolicyState` | Local audit policy state by category/subcategory. |
| `Cmdb.LocalAccount` | Local accounts and SIDs. |
| `Cmdb.LocalGroup` | Local groups and group SIDs. |
| `Cmdb.LocalGroupMember` | Local group membership. |
| `Cmdb.Service` | Installed services and service SIDs. |
| `Cmdb.ProcessLifecycle` | Time-bounded process lifecycle facts. |
| `Cmdb.Certificate` | Local certificate store facts. |
| `Cmdb.ScheduledTask` | Scheduled task definitions. |
| `Cmdb.InstalledSoftware` | Installed software inventory. |
| `Cmdb.Driver` | Installed driver inventory. |
| `Cmdb.NetworkInterface` | Network adapter and address context. |

CMDB data should be joined when needed. It should not be copied into every event row.

Example:

```sql
select
    e.Timestamp,
    e.HostId,
    e.SubjectUserSid,
    a.AccountName,
    a.DomainName
from Silver.WindowsSecurityEvent e
left join Cmdb.LocalAccount a
    on e.HostId = a.HostId
   and e.SubjectUserSid = a.Sid;
```

### 5. Rule Dependency Metadata

The Platform owns rule dependency metadata.

Each rule should declare required log keys:

```text
SourceType
Channel
Provider
EventId
Version, if needed
Required fields
Purpose
Required retention window
```

Example:

```text
Rule: Suspicious process command line
Requires:
  WindowsEventLog / Security / Microsoft-Windows-Security-Auditing / 4688
Purpose:
  Detection
```

The Agent should not own this rule dependency model.

### 6. Coverage Evaluation

The Platform computes coverage by joining:

```text
agent local observations
CMDB state
lookup catalogs
rule dependency metadata
storage state
alert outcomes
historical baselines
```

Derived tables:

```text
Analytics.CollectionCoverageEvaluation
Analytics.RuleCoverageEvaluation
Analytics.LogUtilization
Analytics.CollectionRecommendation
```

### 7. Opportunity Cost

Opportunity cost is computed centrally.

Examples:

| Gap | Example |
| --- | --- |
| Generation gap | Rule requires 4688 but Audit Process Creation is disabled. |
| Input gap | Source exists but Agent does not read it. |
| Filter gap | Agent reads 4688 but filter discards it. |
| Forwarding gap | Agent keeps event but output/buffer rejects it. |
| Storage gap | Platform receives event but retention is too short. |
| Rule gap | Rule requires a log key that is unavailable. |
| Alert gap | Rule had required inputs but no expected alert outcome occurred. |

The Agent can provide counts. The Platform decides whether a missing record creates opportunity cost.

### 8. Noise Cost and Noise Ratio

Noise cost is also computed centrally.

Examples:

| Noise pattern | Meaning |
| --- | --- |
| No-purpose forwarding | Agent forwards logs with no declared downstream purpose. |
| No-purpose storage | Platform stores logs with no rule, enrichment, investigation, compliance, or health purpose. |
| High-volume low-value telemetry | High event volume with low analytical value. |
| Misaligned profile | Collection profile reads sources not used by enabled content. |
| Ineffective filter | Filter keeps too much no-purpose data. |

Noise ratio:

```text
noiseRatio = noiseCount / observedPopulation
```

The observed population must be selected by the Platform:

```text
actualReadCount
keptAfterFilterCount
outputAcceptedCount
storedCount
storedBytes
storedByteDays
```

The Agent should not decide the final noise ratio because it does not know rule usage, storage value, or historical baselines.

### 9. Recommendations

The Platform generates recommendations.

Examples:

```text
Enable Audit Process Creation success auditing.
Add Security 4688 to the kept Event IDs for this profile.
Disable or scope out a rule that depends on unavailable telemetry.
Reduce forwarding for high-volume no-purpose Event IDs.
Increase retention for logs required by active hunting content.
Fix source read permissions for the Security channel.
```

The Agent may report local facts that support a recommendation, but it should not generate authoritative recommendations.

## AuditBuddy-Inspired Role

AuditBuddy is relevant as a reference for Windows audit policy interrogation, especially generation-state checks. It helps answer whether the host was configured to generate the event before the Agent tried to read it. It does not solve end-to-end coverage, filtering quality, forwarding success, SIEM rule coverage, alert creation, or noise/opportunity scoring.

The upstream repository is archived and read-only as of June 11, 2026. Treat it as a reference implementation, not a dependency to adopt directly. Its README describes PowerShell cmdlets for managing Windows audit settings with a .NET library usable by other .NET applications. The library project targets .NET Framework 4.7.2, which is another reason not to embed it directly into a modern .NET 10 DeltaZulu component without redesign.

Its role in DeltaZulu should be:

```text
AuditBuddy-like provider -> Cmdb.AuditPolicyState
```

Not:

```text
AuditBuddy-like provider -> final coverage decision
```

An AuditBuddy-inspired component can collect:

```text
audit category
audit subcategory
subcategory GUID
success enabled
failure enabled
collection timestamp
host identity
```

The Platform then joins that state with:

```text
Reference.WindowsAuditEventLookup
Content.RuleLogRequirement
Agent pipeline observations
Storage state
Alert outcomes
```

Example:

```text
Rule requires Security 4688.
Reference maps 4688 to Audit Process Creation.
Cmdb.AuditPolicyState says Audit Process Creation Success = disabled.
Agent read count for 4688 is zero.
Platform result: BlockedByGeneration.
```

## Data Flow

```text
Endpoint
  Windows Event Log / ETW
        ↓
  DeltaZulu Agent
        ↓
  Raw events + local observations
        ↓
Platform Bronze
  Raw immutable records
        ↓
Platform Silver
  Typed fields
  *_resolved lookup fields
        ↓
CMDB namespace
  Host-local state
  Audit policy state
  Provider/channel state
        ↓
Analytics
  Coverage evaluation
  Noise cost
  Opportunity cost
  Rule usability
        ↓
Golden
  Analyst-facing views
  Dashboards
  Recommendations
```

## Concrete Boundary Rules

### Rule 1: Raw facts stay raw

The Agent and Bronze must preserve source-native values.

```json
{
  "Status": "0xC000006A"
}
```

### Rule 2: Static lookup resolution belongs to Silver

```json
{
  "Status": "0xC000006A",
  "Status_resolved": "STATUS_WRONG_PASSWORD"
}
```

### Rule 3: Host-local context belongs to CMDB

```sql
select *
from Silver.WindowsSecurityEvent e
left join Cmdb.LocalAccount a
    on e.HostId = a.HostId
   and e.SubjectUserSid = a.Sid;
```

### Rule 4: Final evaluation belongs to Analytics

```text
Analytics.CollectionCoverageEvaluation
Analytics.RuleCoverageEvaluation
Analytics.CollectionRecommendation
```

### Rule 5: Agent-local state is evidence, not verdict

The Agent can say:

```text
I read 100 events.
I kept 40 events.
I discarded 60 events.
Audit Process Creation success auditing is disabled.
Security channel is readable.
Output accepted 39 events.
```

The Platform decides:

```text
This created opportunity cost.
This created noise cost.
This rule is blocked.
This profile is inefficient.
This recommendation should be created.
```

## Recommended Record Kinds

### Agent-emitted records

```text
collector.source.health
collector.pipeline.counts
collector.filter.summary
collector.forwarder.health
collector.coverage.local_state
collector.audit_policy.state
collector.event_channel.state
collector.event_provider.state
```

### Platform-derived records/tables

```text
Analytics.CollectionCoverageEvaluation
Analytics.RuleCoverageEvaluation
Analytics.LogUtilization
Analytics.CollectionRecommendation
```

### CMDB tables

```text
Cmdb.Host
Cmdb.EventChannel
Cmdb.EventProvider
Cmdb.AuditPolicyState
Cmdb.LocalAccount
Cmdb.LocalGroup
Cmdb.Service
Cmdb.ProcessLifecycle
```

## Example Scenario

Rule:

```text
Detect suspicious process command line.
Requires Security 4688.
```

Reference:

```text
Security 4688 maps to Audit Process Creation.
Required audit policy: Success.
```

CMDB:

```text
Host A:
  Audit Process Creation Success = disabled
```

Agent observations:

```text
Security channel readable = true
4688 readCount = 0
```

Platform evaluation:

```text
GenerationSupport = false
ReadSupport = false
BlockingGap = GenerationGap
OpportunityCost = present
RuleStatus = BlockedByGeneration
```

Recommendation:

```text
Enable Audit Process Creation success auditing for Host A or remove/scoped-disable rules that depend on Security 4688 for this profile.
```

## Summary

The Agent should collect and forward facts. It can provide bounded local state linked to CMDB entities, including source health, audit policy state, pipeline counts, filter counts, and forwarding state.

The Platform should own stateful interpretation. It should normalize fields, resolve deterministic values, maintain CMDB tables, join rule dependencies, evaluate alert outcomes, calculate opportunity and noise costs, and generate recommendations.

This separation keeps the Agent small and reliable while allowing the Platform to provide full collection coverage evaluation for DeltaZulu.
