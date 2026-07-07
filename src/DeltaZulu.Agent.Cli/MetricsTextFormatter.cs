using System.Globalization;

namespace DeltaZulu.Agent.Cli;

internal static class MetricsTextFormatter
{
    public static string Format(MetricsStateSnapshot snapshot)
    {
        if (!snapshot.Connected)
        {
            return $"""
DeltaZulu agent metrics

State: Disconnected
Path:  {snapshot.Path}
Error: {snapshot.ErrorMessage}

The daemon has not published SQLite metrics state yet, or the controller cannot read it.
""";
        }

        var accepted = Long(snapshot, "records_accepted_total");
        var rejected = Long(snapshot, "records_rejected_total");
        var dropped = Long(snapshot, "records_dropped_total");
        var sendAttempts = Long(snapshot, "transport_send_attempts_total");
        var sendSuccesses = Long(snapshot, "transport_send_successes_total");
        var transientFailures = Long(snapshot, "transport_transient_failures_total");
        var permanentFailures = Long(snapshot, "transport_permanent_failures_total");

        return $"""
DeltaZulu agent metrics

State: Connected
Path:  {snapshot.Path}
Observed UTC: {snapshot.ObservedAtUtc:O}
Agent: {Text(snapshot, "agent_id")}
Host:  {Text(snapshot, "host_id")}

Buffer
  State:              {Text(snapshot, "buffer_state")}
  Disk bytes:         {LongText(snapshot, "disk_bytes_used")} / {LongText(snapshot, "disk_bytes_limit")}
  Memory bytes:       {LongText(snapshot, "memory_bytes_used")}
  Open chunk bytes:   {LongText(snapshot, "open_chunk_bytes")}
  Sealed chunks:      {LongText(snapshot, "sealed_chunk_count")}
  Oldest chunk age:   {DoubleText(snapshot, "oldest_chunk_age_ms")} ms

Records
  Accepted:           {FormatLong(accepted)}
  Rejected:           {FormatLong(rejected)}
  Dropped:            {FormatLong(dropped)}

Chunks
  Completed:          {LongText(snapshot, "chunks_completed_total")}
  Released:           {LongText(snapshot, "chunks_released_total")}
  Dead-lettered:      {LongText(snapshot, "chunks_dead_lettered_total")}

Output
  Last activity UTC:  {Text(snapshot, "last_forwarder_activity_utc")}
  Transport running:  {BoolText(snapshot, "transport_is_running")}
  Send attempts:      {FormatLong(sendAttempts)}
  Send successes:     {FormatLong(sendSuccesses)}
  Transient failures: {FormatLong(transientFailures)}
  Permanent failures: {FormatLong(permanentFailures)}
  Dead-lettered:      {LongText(snapshot, "transport_chunks_dead_lettered_total")}
  Discarded:          {LongText(snapshot, "transport_chunks_discarded_total")}
""";
    }

    public static string FormatDashboard(MetricsStateSnapshot snapshot)
    {
        var observed = snapshot.Connected ? snapshot.ObservedAtUtc.ToString("O", CultureInfo.InvariantCulture) : "-";
        var state = snapshot.Connected ? "Connected" : "Disconnected";
        var health = snapshot.Connected ? Health(snapshot) : "Disconnected";
        var accepted = LongText(snapshot, "records_accepted_total");
        var rejected = LongText(snapshot, "records_rejected_total");
        var dropped = LongText(snapshot, "records_dropped_total");
        var chunksCompleted = LongText(snapshot, "chunks_completed_total");
        var chunksReleased = LongText(snapshot, "chunks_released_total");
        var chunksDeadLettered = LongText(snapshot, "chunks_dead_lettered_total");
        var sendAttempts = LongText(snapshot, "transport_send_attempts_total");
        var sendSuccesses = LongText(snapshot, "transport_send_successes_total");
        var transientFailures = LongText(snapshot, "transport_transient_failures_total");
        var permanentFailures = LongText(snapshot, "transport_permanent_failures_total");

        if (!snapshot.Connected)
        {
            return $"""
┌ DeltaZulu Agent Metrics ─────────────────────────────────────────────────────┐
│ Status: {Pad(state, 16)} Health: {Pad(health, 14)} Observed: {Pad(observed, 24)} │
├ Connection ──────────────────────────────────────────────────────────────────┤
│ SQLite: {snapshot.Path}
│ Error:  {snapshot.ErrorMessage}
│
│ The daemon has not published metrics state yet, or dzagentctl cannot read it. │
│ Configure diagnostics.sqliteFile and wait for the daemon SQLite interval.     │
└──────────────────────────────────────────────────────────────────────────────┘
Keys: Esc close | Ctrl+Q quit | Ctrl+R refresh (planned)
""";
        }

        return $"""
┌ DeltaZulu Agent Metrics ─────────────────────────────────────────────────────┐
│ Status: {Pad(state, 16)} Health: {Pad(health, 14)} Observed: {Pad(observed, 24)} │
│ Agent:  {Pad(Text(snapshot, "agent_id"), 24)} Host: {Pad(Text(snapshot, "host_id"), 24)} │
├ Pipeline ───────────────────────────────┬ Buffer ────────────────────────────┤
│ Logs accepted      {PadLeft(accepted, 14)} │ State          {Pad(Text(snapshot, "buffer_state"), 14)} │
│ Logs rejected      {PadLeft(rejected, 14)} │ Disk bytes     {PadLeft(LongText(snapshot, "disk_bytes_used"), 14)} │
│ Logs dropped       {PadLeft(dropped, 14)} │ Disk limit     {PadLeft(LongText(snapshot, "disk_bytes_limit"), 14)} │
│ Chunks completed   {PadLeft(chunksCompleted, 14)} │ Memory bytes   {PadLeft(LongText(snapshot, "memory_bytes_used"), 14)} │
│ Chunks released    {PadLeft(chunksReleased, 14)} │ Open chunk     {PadLeft(LongText(snapshot, "open_chunk_bytes"), 14)} │
│ Chunks dead-letter {PadLeft(chunksDeadLettered, 14)} │ Sealed chunks  {PadLeft(LongText(snapshot, "sealed_chunk_count"), 14)} │
├ Output ─────────────────────────────────┴ Recent Activity ──────────────────┤
│ Transport running  {Pad(BoolText(snapshot, "transport_is_running"), 14)} Send attempts   {PadLeft(sendAttempts, 14)} │
│ Send successes     {PadLeft(sendSuccesses, 14)} Transient fails {PadLeft(transientFailures, 14)} │
│ Permanent fails    {PadLeft(permanentFailures, 14)} Output dead-ltr {PadLeft(LongText(snapshot, "transport_chunks_dead_lettered_total"), 14)} │
│ Output discarded   {PadLeft(LongText(snapshot, "transport_chunks_discarded_total"), 14)} Last output UTC {Pad(Text(snapshot, "last_forwarder_activity_utc"), 24)} │
├ Source Metrics ──────────────────────────────────────────────────────────────┤
│ Per-source read / filtered / dropped counters are modeled in the pipeline but │
│ are not persisted in this SQLite dashboard yet.                              │
└──────────────────────────────────────────────────────────────────────────────┘
Keys: Esc close | Ctrl+Q quit | Ctrl+R refresh (planned)
""";
    }

    private static string Text(MetricsStateSnapshot snapshot, string key) =>
        snapshot.Values.TryGetValue(key, out var value) ? value?.ToString() ?? "-" : "-";

    private static long? Long(MetricsStateSnapshot snapshot, string key)
    {
        if (!snapshot.Values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            _ => long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null
        };
    }

    private static string LongText(MetricsStateSnapshot snapshot, string key) => FormatLong(Long(snapshot, key));

    private static string FormatLong(long? value) => value?.ToString("N0", CultureInfo.InvariantCulture) ?? "-";

    private static string Health(MetricsStateSnapshot snapshot)
    {
        if (Bool(snapshot, "transport_is_running") == false)
        {
            return "Error";
        }

        return Text(snapshot, "buffer_state").Equals("Healthy", StringComparison.OrdinalIgnoreCase)
            ? "OK"
            : "Degraded";
    }

    private static bool? Bool(MetricsStateSnapshot snapshot, string key)
    {
        if (!snapshot.Values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            bool b => b,
            long l => l != 0,
            int i => i != 0,
            _ => bool.TryParse(value.ToString(), out var parsed) ? parsed : null
        };
    }

    private static string Pad(string? value, int width)
    {
        var text = string.IsNullOrEmpty(value) ? "-" : value;
        return text.Length > width ? text[..width] : text.PadRight(width);
    }

    private static string PadLeft(string? value, int width)
    {
        var text = string.IsNullOrEmpty(value) ? "-" : value;
        return text.Length > width ? text[^width..] : text.PadLeft(width);
    }

    private static string DoubleText(MetricsStateSnapshot snapshot, string key)
    {
        if (!snapshot.Values.TryGetValue(key, out var value) || value is null)
        {
            return "-";
        }

        return value switch
        {
            double d => d.ToString("N0", CultureInfo.InvariantCulture),
            float f => f.ToString("N0", CultureInfo.InvariantCulture),
            _ => double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed.ToString("N0", CultureInfo.InvariantCulture)
                : "-"
        };
    }

    private static string BoolText(MetricsStateSnapshot snapshot, string key)
    {
        if (!snapshot.Values.TryGetValue(key, out var value) || value is null)
        {
            return "-";
        }

        return value switch
        {
            bool b => b ? "yes" : "no",
            long l => l != 0 ? "yes" : "no",
            int i => i != 0 ? "yes" : "no",
            _ => value.ToString() ?? "-"
        };
    }
}
