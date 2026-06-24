#if WINDOWS
#endif

namespace DeltaZulu.Agent.Cli;

internal static partial class Program
{
    private sealed record ResourceSchemaDescription(
        string Id,
        string Name,
        string Version,
        bool Enabled,
        string Source,
        string Platform,
        string Family,
        string? Service,
        string? Channel,
        string? Provider,
        string Table,
        string Schema);
}
