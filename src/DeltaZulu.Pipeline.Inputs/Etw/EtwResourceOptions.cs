namespace DeltaZulu.Pipeline.Inputs.Etw;

public sealed record EtwResourceOptions
{
    public List<int> EventIds { get; init; } = [];
    public List<int> ExcludedEventIds { get; init; } = [];
    public List<int> Opcodes { get; init; } = [];
    public List<int> Versions { get; init; } = [];
    public bool CaptureStacks { get; init; }
    public List<int> StackEventIds { get; init; } = [];
    public List<int> ExcludedStackEventIds { get; init; } = [];
    public List<int> ProcessIds { get; init; } = [];
    public List<string> ProcessNames { get; init; } = [];
    public bool EnableInContainers { get; init; }
    public bool EnableSourceContainerTracking { get; init; }
}
