namespace DeltaZulu.Pipeline.Core.Planning;

public sealed class ExecutionPlanCompilationException : Exception
{
    public ExecutionPlanCompilationException(string message)
        : base(message)
    {
    }
}
