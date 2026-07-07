using System.Diagnostics;

namespace DeltaZulu.Agent.Cli;

internal static class ServiceControlCommand
{
    public static int Run(string[] args)
    {
        var command = args[0];
        var verbose = args.Any(arg => arg is "-v" or "--verbose");
        var serviceName = CliOptions.GetOption(args, "--service") ?? "dzagentd";

        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine($"error: service control command '{command}' is only supported on platforms with systemctl-compatible service management.");
            Console.Error.Flush();
            return 1;
        }

        var startInfo = new ProcessStartInfo("systemctl", [command, serviceName])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("failed to start systemctl.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();

        if (verbose || command == "status" || process.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(output))
            {
                Console.Write(output);
            }
            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.Error.Write(error);
            }
        }

        Console.Out.Flush();
        Console.Error.Flush();
        return process.ExitCode;
    }
}
