namespace DeltaZulu.Agent.Cli;

internal static partial class Program
{
    private const string Version = "0.1.0";

    public static int Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        if (args[0] is "--version" or "version")
        {
            Console.WriteLine($"DeltaZulu.Agent {Version}");
            Console.Out.Flush();
            return 0;
        }

        try
        {
            if (IsModeCommand(args[0]))
            {
                return RunModeCommand(args);
            }

            if (IsServiceCommand(args[0]))
            {
                return RunServiceCommand(args);
            }

            if (IsRemovedLegacyCommand(args[0]))
            {
                Console.Error.WriteLine($"error: '{args[0]}' is no longer a public dzagentctl command. Use --tui to refine profiles, --tail for a focused file-follow query, or --metrics for local daemon state.");
                Console.Error.Flush();
                return 1;
            }

            Console.Error.WriteLine($"error: unknown dzagentctl command '{args[0]}'. Use --help for supported controller modes.");
            Console.Error.Flush();
            return 1;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Console.Error.WriteLine($"error: {ex.GetBaseException().Message}");
            Console.Error.Flush();
            return 1;
        }
    }

    private static bool IsHelp(string value) => value is "-h" or "--help" or "help";

    private static bool IsRemovedLegacyCommand(string value) => value is
        "schema" or "schemas" or "resources" or "list-schemas" or "list-resources" or
        "provider" or "providers" or "run" or
        "syslog" or "syslogserver" or "fifo" or "csv" or "auditd" or
        "eventlog" or "evtx" or "etl" or "etw";

    private static void PrintUsage()
    {
        Console.WriteLine("""
Usage:
  dzagentctl --tui [--profiles <profiles-dir>] [--source <path-or-resource>]
  dzagentctl --tail "<query>" <path> [--input auto|syslog|auditd|csv|lines] [--limit <rows>]
  dzagentctl --metrics [--sqlite <path>]
  dzagentctl start|stop|restart|status|reload [-v] [--service <name>]
  dzagentctl --help
  dzagentctl --version

Controller modes:
  --tui                    Open the profile query workbench. It loads daemon profiles, binds a selected profile to real local data, and helps refine KQL.
  --tail "<query>" <path>  Follow a local file-backed source and show matching rows in a bounded TUI table.
  --metrics                Open the local daemon health dashboard from SQLite state.
  start/stop/restart/status/reload
                           Control the dzagentd service through the OS service manager.

Removed commands:
  schema/provider/run/raw input commands are intentionally not public. Use --tui for profile authoring and source binding.
""");
        Console.Out.Flush();
    }
}
