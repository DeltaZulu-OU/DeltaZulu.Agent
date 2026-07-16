namespace DeltaZulu.Pipeline.Inputs.Syslog;

internal static class SyslogPriority
{
    private static readonly string[] Facilities =
    [
        "kern", "user", "mail", "daemon", "auth", "syslog", "lpr", "news",
        "uucp", "clock", "authpriv", "ftp", "ntp", "audit", "alert", "clock2",
        "local0", "local1", "local2", "local3", "local4", "local5", "local6", "local7"
    ];

    private static readonly string[] Severities =
    [
        "emerg", "alert", "crit", "err", "warning", "notice", "info", "debug"
    ];

    public static (string Facility, string Severity) Decode(int priority)
    {
        var facility = priority / 8;
        var severity = priority % 8;
        return (
            facility >= 0 && facility < Facilities.Length ? Facilities[facility] : facility.ToString(),
            severity >= 0 && severity < Severities.Length ? Severities[severity] : severity.ToString());
    }
}
