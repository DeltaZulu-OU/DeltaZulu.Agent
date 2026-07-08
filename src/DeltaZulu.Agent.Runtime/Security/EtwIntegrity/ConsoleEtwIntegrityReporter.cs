namespace DeltaZulu.Agent.Runtime.Security.EtwIntegrity;

public sealed class ConsoleEtwIntegrityReporter : IEtwIntegrityReporter
{
    public ValueTask ReportAsync(EtwIntegrityFinding finding, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[{finding.ObservedAtUtc:O}] ETW integrity finding");
        Console.WriteLine($"Function: {finding.ModuleName}!{finding.FunctionName}");
        Console.WriteLine($"Address: 0x{finding.LiveAddress.ToInt64():X}");
        Console.WriteLine($"Pattern: {finding.Pattern}");
        Console.WriteLine($"Detail: {finding.Detail}");
        Console.WriteLine($"Baseline: {Convert.ToHexString(finding.BaselineBytes)}");
        Console.WriteLine($"Current:  {Convert.ToHexString(finding.CurrentBytes)}");

        return ValueTask.CompletedTask;
    }
}
