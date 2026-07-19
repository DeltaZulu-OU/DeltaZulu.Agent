namespace DeltaZulu.Pipeline.Core.Planning;

public sealed record ResourceAcquisitionPlan(
    string AcquisitionKey,
    string Kind,
    string Framing,
    string PayloadFormat,
    string AdmissionPolicy,
    string ParserDomain,
    IReadOnlyList<string> ProfileIds);
