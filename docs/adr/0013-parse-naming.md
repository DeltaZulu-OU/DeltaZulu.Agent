# ADR 0013: Naming — DeltaZulu.Parse (renamed from DeltaZulu.Normalize)

## Status

Accepted.

## Context

`DeltaZulu.Normalize` names the assembly whose actual contract is
grammar-driven typed extraction: it maps unstructured and semi-structured
plaintext to catalog-typed values via liblognorm-style parser grammars. That
contract is not "normalization" in the industry sense — canonical field
naming, semantic mapping — which the architecture deliberately defers to an
optional future query-time view layer (ADR 0005). Naming the parser library
"Normalize" invites both readers and future implementers to conflate typed
extraction with semantic canonicalization, and reserves a name the future
semantic layer will need.

"Decoder" was considered and rejected: it imports Wazuh's mental model of the
stage, which does not match DeltaZulu's grammar-driven, catalog-typed
contract.

## Decision

The library is renamed `DeltaZulu.Parse`. "Normalize"/"normalization" is
reserved for the deferred semantic view layer, where the industry meaning of
the word applies. The unit noun for a single grammar is "parser" — liblognorm's
own term for the same concept — so the rename removes the naming confusion
without discarding the liblognorm lineage, which remains credited in
documentation (ADR 0006, ADR 0007). "Rulebase" is retained as the term for a
compiled set of parser rules.

The rename is executed as an assembly, namespace, and prose change:
- `src/DeltaZulu.Normalize` → `src/DeltaZulu.Parse`
  (`DeltaZulu.Normalize.csproj` → `DeltaZulu.Parse.csproj`,
  `NormalizeAssemblyMarker` → `ParseAssemblyMarker`).
- All engineering-document references to the library ("Normalize",
  "Normalize PDAG", "Normalize rule") are disambiguated to "Parse" / "Parse
  PDAG" / "parser rule".
- The `parse.query` profile grammar verb renames from `normalize <field> with
  (...)` to `parse <field> with (...)` for the same reason; this is a
  documentation-only change today because the PDAG compiler is not yet
  implemented (ROADMAP.md Phase 6-7).

## Consequences

No functional change: at the time of this rename, `DeltaZulu.Parse` was only
an assembly-boundary scaffold (ROADMAP.md Phase 1), so the rename was
mechanical. The scaffold project has since moved out of this repository and is
consumed as a package boundary. Future documents must not reintroduce
"normalize"/"normalization" as a name for typed extraction; that vocabulary is reserved for the semantic
layer per ADR 0005, and its trigger for un-deferral is a portable
detection-pack requirement, not this rename.
