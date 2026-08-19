using Microsoft.Data.Sqlite;

namespace DeltaZulu.Agent.DoomDisplay;

public interface IAgentHealthSource
{
    /// <summary>Reads the current agent health, or an unavailable snapshot explaining why not.</summary>
    AgentHealthSnapshot Read();
}

/// <summary>
/// Reads the agent daemon's published SQLite metrics state read-only, the same
/// <c>forwarder_health</c> row and column set <c>dzagentctl metrics</c> reads. It never writes to
/// the file and never holds the connection open between reads, so it cannot interfere with the
/// daemon that owns it.
/// </summary>
public sealed class SqliteAgentHealthSource(string databasePath) : IAgentHealthSource
{
    private const string HealthQuery = """
SELECT
    observed_utc,
    agent_id,
    host_id,
    buffer_state,
    disk_bytes_used,
    disk_bytes_limit,
    memory_bytes_used,
    sealed_chunk_count,
    oldest_chunk_age_ms,
    records_accepted_total,
    records_rejected_total,
    records_dropped_total,
    chunks_completed_total,
    chunks_dead_lettered_total,
    last_forwarder_activity_utc,
    transport_send_attempts_total,
    transport_send_successes_total,
    transport_transient_failures_total,
    transport_permanent_failures_total,
    transport_is_running
FROM forwarder_health
WHERE id = 1;
""";

    private readonly string databasePath = string.IsNullOrWhiteSpace(databasePath)
        ? throw new ArgumentException("An agent metrics database path is required.", nameof(databasePath))
        : databasePath;

    public string DatabasePath => databasePath;

    public AgentHealthSnapshot Read()
    {
        if (!File.Exists(databasePath))
        {
            return AgentHealthSnapshot.Unavailable($"No agent metrics state at {databasePath}.");
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared
            };

            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();

            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA busy_timeout=250;";
                _ = pragma.ExecuteNonQuery();
            }

            using var command = connection.CreateCommand();
            command.CommandText = HealthQuery;
            using var reader = command.ExecuteReader();
            return reader.Read()
                ? Map(reader)
                : AgentHealthSnapshot.Unavailable("Agent metrics state has no forwarder health row yet.");
        }
        catch (SqliteException exception)
        {
            return AgentHealthSnapshot.Unavailable(exception.Message);
        }
        catch (IOException exception)
        {
            return AgentHealthSnapshot.Unavailable(exception.Message);
        }
    }

    private static AgentHealthSnapshot Map(SqliteDataReader reader) => new() {
        Available = true,
        ObservedAtUtc = Timestamp(reader, 0),
        AgentId = Text(reader, 1),
        HostId = Text(reader, 2),
        BufferState = Text(reader, 3),
        DiskBytesUsed = Integer(reader, 4),
        DiskBytesLimit = Integer(reader, 5),
        MemoryBytesUsed = Integer(reader, 6),
        SealedChunkCount = Integer(reader, 7),
        OldestChunkAgeMilliseconds = Real(reader, 8),
        RecordsAcceptedTotal = Integer(reader, 9),
        RecordsRejectedTotal = Integer(reader, 10),
        RecordsDroppedTotal = Integer(reader, 11),
        ChunksCompletedTotal = Integer(reader, 12),
        ChunksDeadLetteredTotal = Integer(reader, 13),
        LastForwarderActivityUtc = Timestamp(reader, 14),
        TransportSendAttemptsTotal = Integer(reader, 15),
        TransportSendSuccessesTotal = Integer(reader, 16),
        TransportTransientFailuresTotal = Integer(reader, 17),
        TransportPermanentFailuresTotal = Integer(reader, 18),
        TransportIsRunning = Integer(reader, 19) is { } running ? running != 0 : null
    };

    private static string? Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static long? Integer(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static double? Real(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static DateTimeOffset? Timestamp(SqliteDataReader reader, int ordinal) =>
        !reader.IsDBNull(ordinal) && DateTimeOffset.TryParse(reader.GetString(ordinal), out var parsed)
            ? parsed
            : null;
}
