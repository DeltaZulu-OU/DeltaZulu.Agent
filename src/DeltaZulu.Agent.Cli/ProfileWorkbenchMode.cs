using DeltaZulu.Agent.ProfileWorkbench;

namespace DeltaZulu.Agent.Cli;

internal static class ProfileWorkbenchMode
{
    public static int Run(string[] args, ControllerModeContext context)
    {
        if (!context.UseTerminalGui)
        {
            Console.Error.WriteLine("error: --tui requires an interactive terminal. Use a compatible terminal and do not pipe stdin.");
            Console.Error.Flush();
            return 1;
        }

        var profilesPath = CliOptions.GetOption(args, "--profiles") ?? "profiles";
        var source = CliOptions.GetOption(args, "--source");
        var library = new ProfileLibrary(profilesPath);
        var profiles = library.ListProfiles();
        if (profiles.Count == 0)
        {
            Console.Error.WriteLine($"error: no enabled, applicable resource profiles were found under '{profilesPath}'.");
            Console.Error.WriteLine("Disabled profiles, platform-mismatched profiles, condition-failed profiles, and unavailable resources are intentionally hidden from the workbench catalog.");
            Console.Error.Flush();
            return 1;
        }

        var registry = new WorkbenchSourceRegistry(context.Warn);
        var runner = new WorkbenchQueryRunner();
        return ProfileWorkbenchTui.TryRun(library, profiles, registry, runner, source, context.Warn) ? 0 : 1;
    }
}
