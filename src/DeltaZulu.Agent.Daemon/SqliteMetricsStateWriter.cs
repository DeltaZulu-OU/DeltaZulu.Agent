using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using Microsoft.Data.Sqlite;

namespace DeltaZulu.Agent.Daemon;

internal sealed class SqliteMetricsStateWriter : IOutputWriter
{
    private const int SchemaVersion = 1;
    private readonly string _path;
    private readonly Lock _gate = new();

    public SqliteMetricsStateWriter(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("SQLite metrics state path is required.", nameof(path));
        }

        _path = path;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        lock (_gate)
        {
            using var connection = OpenConnection();
            Initialize(connection);
        }
    }

    public string Name => "sqlite-metrics-state";

    public void OnNext(ResourceOutputRecord value)
    {
        try
        {
            if (!IsForwarderHealth(value))
            {
                return;
            }

            lock (_gate)
            {
                using var connection = OpenConnection();
                Initialize(connection);
                using var transaction = connection.BeginTransaction();
                UpsertForwarderHealth(connection, transaction, value);
                UpsertAgentStatus(connection, transaction, value);
                UpsertBufferSummary(connection, transaction, value);
                UpsertOutputSummary(connection, transaction, value);
                UpsertPipelineSummary(connection, transaction, value);
                transaction.Commit();
            }
        }
        catch
        {
            // Metrics state publishing is best-effort and must not fail the daemon pipeline.
        }
    }

    public void OnError(Exception error) { }

    public void OnCompleted() { }

    public void Dispose() { }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        ExecuteNonQuery(connection, "PRAGMA journal_mode=WAL;");
        ExecuteNonQuery(connection, "PRAGMA busy_timeout=250;");
        return connection;
    }

    private static void Initialize(SqliteConnection connection)
    {
        ExecuteNonQuery(connection, """
CREATE TABLE IF NOT EXISTS schema_info (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    schema_version INTEGER NOT NULL,
    updated_utc TEXT NOT NULL
);
""");

        ExecuteNonQuery(connection, """
CREATE TABLE IF NOT EXISTS agent_status (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    observed_utc TEXT NOT NULL,
    agent_id TEXT NULL,
    host_id TEXT NULL,
    health TEXT NULL
);
""");

        ExecuteNonQuery(connection, """
CREATE TABLE IF NOT EXISTS pipeline_summary (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    observed_utc TEXT NOT NULL,
    records_read_total INTEGER NULL,
    chunks_released_total INTEGER NULL,
    records_dropped_total INTEGER NULL,
    last_event_utc TEXT NULL,
    last_output_utc TEXT NULL
);
""");

        ExecuteNonQuery(connection, """
CREATE TABLE IF NOT EXISTS buffer_summary (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    observed_utc TEXT NOT NULL,
    state TEXT NULL,
    disk_bytes_used INTEGER NULL,
    disk_bytes_limit INTEGER NULL,
    memory_bytes_used INTEGER NULL,
    open_chunk_bytes INTEGER NULL,
    sealed_chunk_count INTEGER NULL,
    oldest_chunk_age_ms REAL NULL,
    dead_letter_count INTEGER NULL
);
""");

        ExecuteNonQuery(connection, """
CREATE TABLE IF NOT EXISTS output_summary (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    observed_utc TEXT NOT NULL,
    last_activity_utc TEXT NULL,
    transport_send_attempts_total INTEGER NULL,
    transport_send_successes_total INTEGER NULL,
    transport_transient_failures_total INTEGER NULL,
    transport_permanent_failures_total INTEGER NULL,
    transport_chunks_dead_lettered_total INTEGER NULL,
    transport_chunks_discarded_total INTEGER NULL,
    transport_is_running INTEGER NULL
);
""");

        ExecuteNonQuery(connection, """
CREATE TABLE IF NOT EXISTS relp_forwarder_health (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    observed_utc TEXT NOT NULL,
    agent_id TEXT NULL,
    host_id TEXT NULL,
    buffer_state TEXT NULL,
    disk_bytes_used INTEGER NULL,
    disk_bytes_limit INTEGER NULL,
    memory_bytes_used INTEGER NULL,
    open_chunk_bytes INTEGER NULL,
    sealed_chunk_count INTEGER NULL,
    oldest_chunk_age_ms REAL NULL,
    records_accepted_total INTEGER NULL,
    records_rejected_total INTEGER NULL,
    records_dropped_total INTEGER NULL,
    chunks_completed_total INTEGER NULL,
    chunks_released_total INTEGER NULL,
    chunks_dead_lettered_total INTEGER NULL,
    last_forwarder_activity_utc TEXT NULL,
    transport_send_attempts_total INTEGER NULL,
    transport_send_successes_total INTEGER NULL,
    transport_transient_failures_total INTEGER NULL,
    transport_permanent_failures_total INTEGER NULL,
    transport_chunks_dead_lettered_total INTEGER NULL,
    transport_chunks_discarded_total INTEGER NULL,
    transport_is_running INTEGER NULL
);
""");

        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO schema_info (id, schema_version, updated_utc)
VALUES (1, $schemaVersion, $updatedUtc)
ON CONFLICT(id) DO UPDATE SET
    schema_version = excluded.schema_version,
    updated_utc = excluded.updated_utc;
""";
        command.Parameters.AddWithValue("$schemaVersion", SchemaVersion);
        command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static bool IsForwarderHealth(ResourceOutputRecord value) =>
        TryGetString(value.Metadata, "recordKind") == "collector.forwarder.health"
        || value.Event.ContainsKey("bufferState");

    private static void UpsertForwarderHealth(SqliteConnection connection, SqliteTransaction transaction, ResourceOutputRecord value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO relp_forwarder_health (
    id,
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
    transport_is_running)
VALUES (
    1,
    $observedUtc,
    $agentId,
    $hostId,
    $bufferState,
    $diskBytesUsed,
    $diskBytesLimit,
    $memoryBytesUsed,
    $openChunkBytes,
    $sealedChunkCount,
    $oldestChunkAgeMs,
    $recordsAcceptedTotal,
    $recordsRejectedTotal,
    $recordsDroppedTotal,
    $chunksCompletedTotal,
    $chunksReleasedTotal,
    $chunksDeadLetteredTotal,
    $lastForwarderActivityUtc,
    $transportSendAttemptsTotal,
    $transportSendSuccessesTotal,
    $transportTransientFailuresTotal,
    $transportPermanentFailuresTotal,
    $transportChunksDeadLetteredTotal,
    $transportChunksDiscardedTotal,
    $transportIsRunning)
ON CONFLICT(id) DO UPDATE SET
    observed_utc = excluded.observed_utc,
    agent_id = excluded.agent_id,
    host_id = excluded.host_id,
    buffer_state = excluded.buffer_state,
    disk_bytes_used = excluded.disk_bytes_used,
    disk_bytes_limit = excluded.disk_bytes_limit,
    memory_bytes_used = excluded.memory_bytes_used,
    open_chunk_bytes = excluded.open_chunk_bytes,
    sealed_chunk_count = excluded.sealed_chunk_count,
    oldest_chunk_age_ms = excluded.oldest_chunk_age_ms,
    records_accepted_total = excluded.records_accepted_total,
    records_rejected_total = excluded.records_rejected_total,
    records_dropped_total = excluded.records_dropped_total,
    chunks_completed_total = excluded.chunks_completed_total,
    chunks_released_total = excluded.chunks_released_total,
    chunks_dead_lettered_total = excluded.chunks_dead_lettered_total,
    last_forwarder_activity_utc = excluded.last_forwarder_activity_utc,
    transport_send_attempts_total = excluded.transport_send_attempts_total,
    transport_send_successes_total = excluded.transport_send_successes_total,
    transport_transient_failures_total = excluded.transport_transient_failures_total,
    transport_permanent_failures_total = excluded.transport_permanent_failures_total,
    transport_chunks_dead_lettered_total = excluded.transport_chunks_dead_lettered_total,
    transport_chunks_discarded_total = excluded.transport_chunks_discarded_total,
    transport_is_running = excluded.transport_is_running;
""";

        AddCommonIdentity(command, value);
        Add(command, "$bufferState", TryGetString(value.Event, "bufferState"));
        Add(command, "$diskBytesUsed", TryGetLong(value.Event, "diskBytesUsed"));
        Add(command, "$diskBytesLimit", TryGetLong(value.Event, "diskBytesLimit"));
        Add(command, "$memoryBytesUsed", TryGetLong(value.Event, "memoryBytesUsed"));
        Add(command, "$openChunkBytes", TryGetLong(value.Event, "openChunkBytes"));
        Add(command, "$sealedChunkCount", TryGetLong(value.Event, "sealedChunkCount"));
        Add(command, "$oldestChunkAgeMs", TryGetDouble(value.Event, "oldestChunkAgeMs"));
        Add(command, "$recordsAcceptedTotal", TryGetLong(value.Event, "recordsAcceptedTotal"));
        Add(command, "$recordsRejectedTotal", TryGetLong(value.Event, "recordsRejectedTotal"));
        Add(command, "$recordsDroppedTotal", TryGetLong(value.Event, "recordsDroppedTotal"));
        Add(command, "$chunksCompletedTotal", TryGetLong(value.Event, "chunksCompletedTotal"));
        Add(command, "$chunksReleasedTotal", TryGetLong(value.Event, "chunksReleasedTotal"));
        Add(command, "$chunksDeadLetteredTotal", TryGetLong(value.Event, "chunksDeadLetteredTotal"));
        Add(command, "$lastForwarderActivityUtc", TryGetFormatted(value.Event, "lastForwarderActivityUtc"));
        Add(command, "$transportSendAttemptsTotal", TryGetLong(value.Event, "transportSendAttemptsTotal"));
        Add(command, "$transportSendSuccessesTotal", TryGetLong(value.Event, "transportSendSuccessesTotal"));
        Add(command, "$transportTransientFailuresTotal", TryGetLong(value.Event, "transportTransientFailuresTotal"));
        Add(command, "$transportPermanentFailuresTotal", TryGetLong(value.Event, "transportPermanentFailuresTotal"));
        Add(command, "$transportChunksDeadLetteredTotal", TryGetLong(value.Event, "transportChunksDeadLetteredTotal"));
        Add(command, "$transportChunksDiscardedTotal", TryGetLong(value.Event, "transportChunksDiscardedTotal"));
        Add(command, "$transportIsRunning", TryGetBool(value.Event, "transportIsRunning") is { } running ? running ? 1 : 0 : null);
        command.ExecuteNonQuery();
    }

    private static void UpsertAgentStatus(SqliteConnection connection, SqliteTransaction transaction, ResourceOutputRecord value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO agent_status (id, observed_utc, agent_id, host_id, health)
VALUES (1, $observedUtc, $agentId, $hostId, $health)
ON CONFLICT(id) DO UPDATE SET
    observed_utc = excluded.observed_utc,
    agent_id = excluded.agent_id,
    host_id = excluded.host_id,
    health = excluded.health;
""";
        AddCommonIdentity(command, value);
        Add(command, "$health", ComputeAgentHealth(value));
        command.ExecuteNonQuery();
    }

    private static void UpsertPipelineSummary(SqliteConnection connection, SqliteTransaction transaction, ResourceOutputRecord value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO pipeline_summary (id, observed_utc, records_read_total, chunks_released_total, records_dropped_total, last_event_utc, last_output_utc)
VALUES (1, $observedUtc, $recordsReadTotal, $chunksReleasedTotal, $recordsDroppedTotal, $lastEventUtc, $lastOutputUtc)
ON CONFLICT(id) DO UPDATE SET
    observed_utc = excluded.observed_utc,
    records_read_total = excluded.records_read_total,
    chunks_released_total = excluded.chunks_released_total,
    records_dropped_total = excluded.records_dropped_total,
    last_event_utc = excluded.last_event_utc,
    last_output_utc = excluded.last_output_utc;
""";
        Add(command, "$observedUtc", ObservedUtc(value));
        Add(command, "$recordsReadTotal", TryGetLong(value.Event, "recordsAcceptedTotal"));
        Add(command, "$chunksReleasedTotal", TryGetLong(value.Event, "chunksReleasedTotal"));
        Add(command, "$recordsDroppedTotal", TryGetLong(value.Event, "recordsDroppedTotal"));
        Add(command, "$lastEventUtc", ObservedUtc(value));
        Add(command, "$lastOutputUtc", TryGetFormatted(value.Event, "lastForwarderActivityUtc"));
        command.ExecuteNonQuery();
    }

    private static void UpsertBufferSummary(SqliteConnection connection, SqliteTransaction transaction, ResourceOutputRecord value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO buffer_summary (id, observed_utc, state, disk_bytes_used, disk_bytes_limit, memory_bytes_used, open_chunk_bytes, sealed_chunk_count, oldest_chunk_age_ms, dead_letter_count)
VALUES (1, $observedUtc, $state, $diskBytesUsed, $diskBytesLimit, $memoryBytesUsed, $openChunkBytes, $sealedChunkCount, $oldestChunkAgeMs, $deadLetterCount)
ON CONFLICT(id) DO UPDATE SET
    observed_utc = excluded.observed_utc,
    state = excluded.state,
    disk_bytes_used = excluded.disk_bytes_used,
    disk_bytes_limit = excluded.disk_bytes_limit,
    memory_bytes_used = excluded.memory_bytes_used,
    open_chunk_bytes = excluded.open_chunk_bytes,
    sealed_chunk_count = excluded.sealed_chunk_count,
    oldest_chunk_age_ms = excluded.oldest_chunk_age_ms,
    dead_letter_count = excluded.dead_letter_count;
""";
        Add(command, "$observedUtc", ObservedUtc(value));
        Add(command, "$state", TryGetString(value.Event, "bufferState"));
        Add(command, "$diskBytesUsed", TryGetLong(value.Event, "diskBytesUsed"));
        Add(command, "$diskBytesLimit", TryGetLong(value.Event, "diskBytesLimit"));
        Add(command, "$memoryBytesUsed", TryGetLong(value.Event, "memoryBytesUsed"));
        Add(command, "$openChunkBytes", TryGetLong(value.Event, "openChunkBytes"));
        Add(command, "$sealedChunkCount", TryGetLong(value.Event, "sealedChunkCount"));
        Add(command, "$oldestChunkAgeMs", TryGetDouble(value.Event, "oldestChunkAgeMs"));
        Add(command, "$deadLetterCount", TryGetLong(value.Event, "chunksDeadLetteredTotal"));
        command.ExecuteNonQuery();
    }

    private static void UpsertOutputSummary(SqliteConnection connection, SqliteTransaction transaction, ResourceOutputRecord value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO output_summary (id, observed_utc, last_activity_utc, transport_send_attempts_total, transport_send_successes_total, transport_transient_failures_total, transport_permanent_failures_total, transport_chunks_dead_lettered_total, transport_chunks_discarded_total, transport_is_running)
VALUES (1, $observedUtc, $lastActivityUtc, $transportSendAttemptsTotal, $transportSendSuccessesTotal, $transportTransientFailuresTotal, $transportPermanentFailuresTotal, $transportChunksDeadLetteredTotal, $transportChunksDiscardedTotal, $transportIsRunning)
ON CONFLICT(id) DO UPDATE SET
    observed_utc = excluded.observed_utc,
    last_activity_utc = excluded.last_activity_utc,
    transport_send_attempts_total = excluded.transport_send_attempts_total,
    transport_send_successes_total = excluded.transport_send_successes_total,
    transport_transient_failures_total = excluded.transport_transient_failures_total,
    transport_permanent_failures_total = excluded.transport_permanent_failures_total,
    transport_chunks_dead_lettered_total = excluded.transport_chunks_dead_lettered_total,
    transport_chunks_discarded_total = excluded.transport_chunks_discarded_total,
    transport_is_running = excluded.transport_is_running;
""";
        Add(command, "$observedUtc", ObservedUtc(value));
        Add(command, "$lastActivityUtc", TryGetFormatted(value.Event, "lastForwarderActivityUtc"));
        Add(command, "$transportSendAttemptsTotal", TryGetLong(value.Event, "transportSendAttemptsTotal"));
        Add(command, "$transportSendSuccessesTotal", TryGetLong(value.Event, "transportSendSuccessesTotal"));
        Add(command, "$transportTransientFailuresTotal", TryGetLong(value.Event, "transportTransientFailuresTotal"));
        Add(command, "$transportPermanentFailuresTotal", TryGetLong(value.Event, "transportPermanentFailuresTotal"));
        Add(command, "$transportChunksDeadLetteredTotal", TryGetLong(value.Event, "transportChunksDeadLetteredTotal"));
        Add(command, "$transportChunksDiscardedTotal", TryGetLong(value.Event, "transportChunksDiscardedTotal"));
        Add(command, "$transportIsRunning", TryGetBool(value.Event, "transportIsRunning") is { } running ? running ? 1 : 0 : null);
        command.ExecuteNonQuery();
    }

    private static string ComputeAgentHealth(ResourceOutputRecord value)
    {
        var bufferState = TryGetString(value.Event, "bufferState");
        var transportRunning = TryGetBool(value.Event, "transportIsRunning");

        if (transportRunning == false)
        {
            return "Error";
        }

        return string.Equals(bufferState, "Healthy", StringComparison.OrdinalIgnoreCase)
            ? "OK"
            : "Degraded";
    }

    private static void AddCommonIdentity(SqliteCommand command, ResourceOutputRecord value)
    {
        Add(command, "$observedUtc", ObservedUtc(value));
        Add(command, "$agentId", TryGetString(value.Metadata, "agentId"));
        Add(command, "$hostId", TryGetString(value.Metadata, "hostId"));
    }

    private static string? ObservedUtc(ResourceOutputRecord value) =>
        FormatValue(value.Metadata.TryGetValue("observedAt", out var observedAt) ? observedAt : DateTimeOffset.UtcNow);

    private static void ExecuteNonQuery(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string? TryGetString(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static string? TryGetFormatted(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) ? FormatValue(value) : null;

    private static string? FormatValue(object? value) => value switch
    {
        null => null,
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O"),
        DateTime dateTime => dateTime.ToString("O"),
        _ => value.ToString()
    };

    private static long? TryGetLong(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            ulong ul when ul <= long.MaxValue => (long)ul,
            uint ui => ui,
            ushort us => us,
            _ => long.TryParse(value.ToString(), out var parsed) ? parsed : null
        };
    }

    private static double? TryGetDouble(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            _ => double.TryParse(value.ToString(), out var parsed) ? parsed : null
        };
    }

    private static bool? TryGetBool(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            _ => bool.TryParse(value.ToString(), out var parsed) ? parsed : null
        };
    }
}
