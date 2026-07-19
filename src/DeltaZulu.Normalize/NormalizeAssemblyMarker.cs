namespace DeltaZulu.Normalize;

/// <summary>
/// Scaffold anchor for the Normalize plaintext parser assembly described in
/// ADR 0006/0007 and ARCHITECTURE.md. The <c>parse.query</c> grammar, unified
/// PDAG compiler, and rule/topic model are ROADMAP.md Phase 6-7 work; this
/// type exists only so ROADMAP.md Phase 1 ("Pipeline references Normalize,
/// LocalStream, and RELP") has a stable, reflectable assembly boundary before
/// that work lands.
/// </summary>
public static class NormalizeAssemblyMarker
{
}
