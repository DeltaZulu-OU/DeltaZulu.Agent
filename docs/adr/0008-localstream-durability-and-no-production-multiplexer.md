# ADR 0008: LocalStream durability boundary and no production multiplexer

## Status

Accepted.

## Context

The current daemon serializes concurrent profile pipelines through an in-memory
`ChannelOutputMultiplexer` and forwards via direct DurableBuffer ownership. This
creates a legacy fan-in topology without a durable materialization/filter
boundary.

## Decision

Use one LocalStream host with `agent.parsed` and `agent.output` as the normal
runtime's only internal physical topics. Parsed envelopes durably separate
materialization from filtering; output rows durably separate filtering from
acknowledged delivery. Logical topics stay in envelopes.

The dispatcher appends outputs before committing a parsed position. The RELP
forwarder commits output only after acknowledgement. The daemon has no
general-purpose multiplexer. A bounded writer inside a LocalStream publisher is
allowed only as a private producer-safety implementation detail.

## Consequences

- LocalStream provides durable fan-in/fan-out, replay, checkpoints, and bounded
  retention; profiles do not configure its topology.
- Direct Pipeline DurableBuffer ownership and daemon multiplexer use are removed
  in their migration phases.
- CLI/workbench may use an isolated serialized writer when necessary, but it is
  not a daemon architecture component.
