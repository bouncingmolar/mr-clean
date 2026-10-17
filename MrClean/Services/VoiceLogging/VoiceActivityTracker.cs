namespace MrClean.Services.VoiceLogging;

public class VoiceSession
{
    public ulong UserId { get; set; }
    public ulong ChannelId { get; set; }
    public ulong Destination { get; set; }
    public string Activity { get; set; } = "";
    public DateTimeOffset Started { get; set; }
    public DateTimeOffset? ConnectedSince { get; set; }
    public DateTimeOffset LastObserved { get; set; }
    public DateTimeOffset? Disconnected { get; set; }
    public double ConnectedSeconds { get; set; }
    public int Interruptions { get; set; }
}

public record VoiceLog(ulong Destination, string Text);

public class VoiceActivityTracker
{
    public long LastLogId { get; set; }
    public List<VoiceSession> Sessions { get; set; } = new();
    public List<VoiceLog> Pending { get; set; } = new();

    private void Log(ulong destination, DateTimeOffset now, string text) =>
        Pending.Add(new VoiceLog(destination, $"<t:{now.ToUnixTimeSeconds()}:T> {text}"));

    public void Transition(ulong user, ulong? channel, string? activity, DateTimeOffset now,
        TimeSpan window, ulong destination, bool uncertain = false, Func<ulong, string?>? activityAtEnd = null)
    {
        var session = Sessions.SingleOrDefault(x => x.UserId == user);
        if (session?.ConnectedSince != null && session.ChannelId == channel) return;
        if (session?.ConnectedSince != null)
        {
            var end = uncertain ? session.LastObserved : now;
            session.ConnectedSeconds += Math.Max(0, (end - session.ConnectedSince.Value).TotalSeconds);
            session.ConnectedSince = null;
            session.Disconnected = end;
            Log(session.Destination == 0 ? destination : session.Destination, now, $"<@{user}> left {Safe(activityAtEnd?.Invoke(session.ChannelId) ?? session.Activity)} (<#{session.ChannelId}>); connected {Duration(session.ConnectedSeconds)} total since <t:{session.Started.ToUnixTimeSeconds()}:F>; {session.Interruptions} brief interruption(s)." +
                (uncertain ? " Observation gap: connection ended at the last confirmed checkpoint; actual departure unknown." : ""));
        }
        if (channel == null || activity == null) return;
        if (session != null && session.ChannelId == channel && now - session.Disconnected <= window)
        {
            session.Interruptions++;
            session.ConnectedSince = now;
            session.LastObserved = now;
            Log(session.Destination == 0 ? destination : session.Destination, now, $"<@{user}> rejoined {Safe(session.Activity)} (<#{channel}>); same session, interruption {Duration((now - session.Disconnected!.Value).TotalSeconds)} excluded.");
            session.Disconnected = null;
            return;
        }
        if (session != null) Sessions.Remove(session);
        Sessions.Add(new VoiceSession { UserId = user, ChannelId = channel.Value, Activity = activity, Destination = destination,
            Started = now, ConnectedSince = now, LastObserved = now });
        Log(destination, now, $"<@{user}> joined {Safe(activity)} (<#{channel}>).");
    }

    public void Checkpoint(DateTimeOffset now, TimeSpan window)
    {
        foreach (var session in Sessions.Where(x => x.ConnectedSince != null)) session.LastObserved = now;
        Sessions.RemoveAll(x => x.Disconnected != null && now - x.Disconnected > window);
    }

    private static string Safe(string value) => new(value.Where(c => !char.IsControl(c)).Take(100).Select(c => c is '`' or '@' or '*' or '_' or '~' or '<' or '>' ? ' ' : c).ToArray());
    private static string Duration(double seconds) => $"{(long)seconds / 3600}h {(long)seconds / 60 % 60}m {(long)seconds % 60}s";
}
