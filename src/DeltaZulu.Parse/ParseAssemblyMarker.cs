namespace DeltaZulu.Parse;

/// <summary>
/// Scaffold anchor for the Parse (grammar-driven typed extraction) assembly
/// described in ADR 0006/0007/0013 and ARCHITECTURE.md. The <c>parse.query</c>
/// grammar, unified PDAG compiler, and rule/topic model are ROADMAP.md Phase
/// 6-7 work; this type exists only so ROADMAP.md Phase 1 ("Pipeline references
/// Parse, LocalStream, and FORWARDER") has a stable, reflectable assembly boundary
/// before that work lands. "Normalize" is reserved for the deferred semantic
/// view layer (ADR 0005); this library's contract is typed extraction, not
/// semantic normalization.
/// </summary>
public static class ParseAssemblyMarker
{
}
