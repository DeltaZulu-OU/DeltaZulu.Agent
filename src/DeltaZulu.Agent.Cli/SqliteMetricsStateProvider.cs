using Microsoft.Data.Sqlite;

namespace DeltaZulu.Agent.Cli;

internal sealed record MetricsStateSnapshot(
    string State,
    string Path,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyDictionary<string, object?> Values,
    string? ErrorMessage = null)
{
    public bool Connected => string.Equals(State, "Connected", StringComparison.OrdinalIgnoreCase);
}

internal static class SqliteMetricsStateProvider
{
    public static MetricsStateSnapshot Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Disconnected(path, "SQLite metrics path is empty.");
        }

        if (!File.Exists(path))
        {
            return Disconnected(path, "SQLite metrics state file does not exist yet.");
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared
            };

            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            ExecuteNonQuery(connection, "PRAGMA busy_timeout=250;");

            using var command = connection.CreateCommand();
            command.CommandText = """
SELECT
    observed_utc,
    agent_id,
    host_id,
    buffer_state,
    disk_bytes_used,
    disk_bytes_limit,
    memory_bytes_used,
    open_chunk_bytes,
    sealed_chunk_count,
    oldest_chunk_age_ms,
    records_accepted_total,
    records_rejected_total,
    records_dropped_total,
    chunks_completed_total,
    chunks_released_total,
    chunks_dead_lettered_total,
    last_forwarder_activity_utc,
    transport_send_attempts_total,
    transport_send_successes_total,
    transport_transient_failures_total,
    transport_permanent_failures_total,
    transport_chunks_dead_lettered_total,
    transport_chunks_discarded_total,
    transport_is_running
FROM relp_forwarder_health
WHERE id = 1;
""";

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return Disconnected(path, "SQLite metrics state has no forwarder health row yet.");
            }

            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                values[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            var observedAt = TryParseDateTimeOffset(values.TryGetValue("observed_utc", out var observed) ? observed?.ToString() : null)
                ?? DateTimeOffset.UtcNow;
            return new MetricsStateSnapshot("Connected", path, observedAt, values);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return Disconnected(path, ex.GetBaseException().Message);
        }
    }

    private static MetricsStateSnapshot Disconnected(string path, string message) =>
        new("Disconnected", path, DateTimeOffset.UtcNow, new Dictionary<string, object?>(), message);

    private static void ExecuteNonQuery(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static DateTimeOffset? TryParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}
