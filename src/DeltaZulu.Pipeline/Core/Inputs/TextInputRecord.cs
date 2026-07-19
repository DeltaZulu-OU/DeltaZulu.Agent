using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Core.Inputs;

/// <summary>
/// Transport/framing output for unstructured text. It carries collection facts and
/// raw text only; application parsing belongs to Normalize.
/// </summary>
public sealed record TextInputRecord(
    ResourceMetadata Metadata,
    string Text,
    string Framing,
    string AdmissionPolicy,
    string ParserDomain);
