namespace DeltaZulu.Agent.DoomDisplay;

/// <summary>
/// Holds the newest agent health reading for the render loop. Health follows the same
/// latest-wins policy as the pixel stream: the display shows current state, never a backlog.
/// The revision counter lets the render loop redraw when health changes even though no new
/// frame arrived.
/// </summary>
public sealed class AgentHealthDisplayState
{
    private AgentHealthSnapshot? current;
    private long revision;
    private long receivedAtTicks;

    /// <summary>Increments on every accepted reading, so a renderer can detect a change cheaply.</summary>
    public long Revision => Interlocked.Read(ref revision);

    public void Update(AgentHealthSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Interlocked.Exchange(ref receivedAtTicks, DateTimeOffset.UtcNow.UtcTicks);
        Volatile.Write(ref current, snapshot);
        Interlocked.Increment(ref revision);
    }

    /// <summary>
    /// Returns the newest reading and how long ago the collector received it, or <see langword="null" />
    /// when the agent has not reported yet.
    /// </summary>
    public AgentHealthView? View()
    {
        var snapshot = Volatile.Read(ref current);
        if (snapshot is null)
        {
            return null;
        }

        var receivedAt = new DateTimeOffset(Interlocked.Read(ref receivedAtTicks), TimeSpan.Zero);
        var age = DateTimeOffset.UtcNow - receivedAt;
        return new AgentHealthView(snapshot, age < TimeSpan.Zero ? TimeSpan.Zero : age);
    }
}

public readonly record struct AgentHealthView(AgentHealthSnapshot Snapshot, TimeSpan ReceivedAgo);
