using DeltaZulu.Agent.DoomDisplay.Inputs;
using DeltaZulu.Forward;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DeltaZulu.Agent.DoomDisplay.Tests;

[TestClass]
public sealed class AgentHealthTests
{
    private string databasePath = string.Empty;

    [TestInitialize]
    public void CreateDatabasePath() =>
        databasePath = Path.Combine(Path.GetTempPath(), $"dzagent-health-{Guid.NewGuid():N}.sqlite");

    [TestCleanup]
    public void RemoveDatabase()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public void Reader_ReportsUnavailableWhenTheDaemonHasNotPublishedYet()
    {
        var snapshot = new SqliteAgentHealthSource(databasePath).Read();

        Assert.IsFalse(snapshot.Available);
        StringAssert.Contains(snapshot.UnavailableReason, databasePath);
    }

    [TestMethod]
    public void Reader_ReportsUnavailableWhenTheHealthRowIsMissing()
    {
        CreateHealthTable();

        var snapshot = new SqliteAgentHealthSource(databasePath).Read();

        Assert.IsFalse(snapshot.Available);
        StringAssert.Contains(snapshot.UnavailableReason, "no forwarder health row");
    }

    [TestMethod]
    public void Reader_MapsThePublishedDaemonRow()
    {
        CreateHealthTable();
        InsertHealthRow();

        var snapshot = new SqliteAgentHealthSource(databasePath).Read();

        Assert.IsTrue(snapshot.Available, snapshot.UnavailableReason);
        Assert.AreEqual("agent-01", snapshot.AgentId);
        Assert.AreEqual("host-01", snapshot.HostId);
        Assert.AreEqual("Healthy", snapshot.BufferState);
        Assert.AreEqual(12_582_912L, snapshot.DiskBytesUsed);
        Assert.AreEqual(1_073_741_824L, snapshot.DiskBytesLimit);
        Assert.AreEqual(3L, snapshot.SealedChunkCount);
        Assert.AreEqual(412.5d, snapshot.OldestChunkAgeMilliseconds);
        Assert.AreEqual(128_441L, snapshot.RecordsAcceptedTotal);
        Assert.AreEqual(61L, snapshot.TransportSendSuccessesTotal);
        Assert.IsTrue(snapshot.TransportIsRunning);
        Assert.IsNotNull(snapshot.ObservedAtUtc);
    }

    [TestMethod]
    public void Reader_LeavesNullColumnsAbsentRatherThanZero()
    {
        CreateHealthTable();
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO forwarder_health (id, observed_utc) VALUES (1, $observed);";
            _ = command.Parameters.AddWithValue("$observed", DateTimeOffset.UtcNow.ToString("O"));
            _ = command.ExecuteNonQuery();
        }

        var snapshot = new SqliteAgentHealthSource(databasePath).Read();

        Assert.IsTrue(snapshot.Available, snapshot.UnavailableReason);
        Assert.IsNull(snapshot.DiskBytesUsed);
        Assert.IsNull(snapshot.TransportIsRunning);
        Assert.IsNull(snapshot.BufferState);
    }

    [TestMethod]
    public void HealthBatch_SurvivesTheTypedForwardContractRoundTrip()
    {
        var source = new AgentHealthSnapshot {
            Available = true,
            ObservedAtUtc = new DateTimeOffset(2026, 8, 19, 10, 30, 0, TimeSpan.Zero),
            AgentId = "agent-01",
            HostId = "host-01",
            BufferState = "Healthy",
            DiskBytesUsed = 12_582_912,
            DiskBytesLimit = 1_073_741_824,
            MemoryBytesUsed = 2_097_152,
            SealedChunkCount = 3,
            OldestChunkAgeMilliseconds = 412.5,
            RecordsAcceptedTotal = 128_441,
            RecordsRejectedTotal = 0,
            RecordsDroppedTotal = 0,
            ChunksCompletedTotal = 61,
            ChunksDeadLetteredTotal = 0,
            TransportIsRunning = true,
            TransportSendAttemptsTotal = 61,
            TransportSendSuccessesTotal = 61,
            TransportTransientFailuresTotal = 0,
            TransportPermanentFailuresTotal = 0
        };

        var payload = ForwardLogBatchCodec.Encode(AgentHealthCodec.ToBatch(source, "agent-01", "host-01"));
        var decoded = new AgentHealthInputDecoder().Decode(payload);

        Assert.IsTrue(decoded.Available);
        Assert.AreEqual(source.AgentId, decoded.AgentId);
        Assert.AreEqual(source.BufferState, decoded.BufferState);
        Assert.AreEqual(source.DiskBytesUsed, decoded.DiskBytesUsed);
        Assert.AreEqual(source.OldestChunkAgeMilliseconds, decoded.OldestChunkAgeMilliseconds);
        Assert.AreEqual(source.RecordsAcceptedTotal, decoded.RecordsAcceptedTotal);
        Assert.AreEqual(source.TransportIsRunning, decoded.TransportIsRunning);
        Assert.AreEqual(source.ObservedAtUtc, decoded.ObservedAtUtc);
    }

    [TestMethod]
    public void HealthBatch_CarriesUnavailabilityRatherThanHidingIt()
    {
        var payload = ForwardLogBatchCodec.Encode(
            AgentHealthCodec.ToBatch(AgentHealthSnapshot.Unavailable("daemon stopped"), "agent-01", "host-01"));

        var decoded = new AgentHealthInputDecoder().Decode(payload);

        Assert.IsFalse(decoded.Available);
        Assert.AreEqual("daemon stopped", decoded.UnavailableReason);
    }

    [TestMethod]
    public void HealthDecoder_RejectsAPayloadThatIsNotAgentHealth()
    {
        var foreignBatch = new ForwardLogBatch {
            BatchId = Guid.NewGuid(),
            Records = [
                new ForwardLogRecord {
                    DeliveryId = "d1",
                    RecordId = "r1",
                    AgentId = "agent-01",
                    SourceType = "windows-event-log",
                    SourceName = "Security",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Fields = new Dictionary<string, object?> { ["eventId"] = 4624L }
                }
            ]
        };

        var decoder = new AgentHealthInputDecoder();

        var exception = Assert.ThrowsExactly<AgentHealthDecodeException>(
            () => decoder.Decode(ForwardLogBatchCodec.Encode(foreignBatch)));
        StringAssert.Contains(exception.Message, "windows-event-log");
    }

    [TestMethod]
    public void HealthDecoder_RejectsMalformedAndEmptyPayloads()
    {
        var decoder = new AgentHealthInputDecoder();

        _ = Assert.ThrowsExactly<AgentHealthDecodeException>(() => decoder.Decode(new byte[] { 0xC1 }));
        _ = Assert.ThrowsExactly<AgentHealthDecodeException>(() => decoder.Decode(ReadOnlyMemory<byte>.Empty));
    }

    [TestMethod]
    public void DisplayState_KeepsOnlyTheNewestReadingAndAdvancesItsRevision()
    {
        var state = new AgentHealthDisplayState();
        Assert.IsNull(state.View());

        state.Update(new AgentHealthSnapshot { Available = true, BufferState = "Healthy" });
        var firstRevision = state.Revision;
        state.Update(new AgentHealthSnapshot { Available = true, BufferState = "Degraded" });

        var view = state.View();
        Assert.IsNotNull(view);
        Assert.AreEqual("Degraded", view.Value.Snapshot.BufferState);
        Assert.IsTrue(state.Revision > firstRevision);
        Assert.IsTrue(view.Value.ReceivedAgo >= TimeSpan.Zero);
    }

    private void CreateHealthTable()
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        // Mirrors the daemon's SqliteMetricsStateWriter schema.
        command.CommandText = """
CREATE TABLE IF NOT EXISTS forwarder_health (
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
""";
        _ = command.ExecuteNonQuery();
    }

    private void InsertHealthRow()
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO forwarder_health (
    id, observed_utc, agent_id, host_id, buffer_state,
    disk_bytes_used, disk_bytes_limit, memory_bytes_used, sealed_chunk_count, oldest_chunk_age_ms,
    records_accepted_total, records_rejected_total, records_dropped_total,
    chunks_completed_total, chunks_dead_lettered_total, last_forwarder_activity_utc,
    transport_send_attempts_total, transport_send_successes_total,
    transport_transient_failures_total, transport_permanent_failures_total, transport_is_running)
VALUES (
    1, $observed, 'agent-01', 'host-01', 'Healthy',
    12582912, 1073741824, 2097152, 3, 412.5,
    128441, 0, 0,
    61, 0, $observed,
    61, 61,
    0, 0, 1);
""";
        _ = command.Parameters.AddWithValue("$observed", DateTimeOffset.UtcNow.ToString("O"));
        _ = command.ExecuteNonQuery();
    }
}
