using MrClean.Models;

namespace MrClean.Services.VoiceLogging;

public class VoiceSession
{
    public VoiceChannelRule? Rule { get; set; }
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

public record VoiceLog(ulong Destination, string Text, bool EmbedOnly = false);

public class VoiceActivityTracker
{
    public long LastLogId { get; set; }
    public List<VoiceSession> Sessions { get; set; } = new();
    public List<VoiceLog> Pending { get; set; } = new();

    private void Join(ulong destination, ulong user, ulong channel) =>
        Pending.Add(new VoiceLog(destination, $"🔊 <@{user}> joined <#{channel}>"));

    public void Transition(ulong user, ulong? channel, string? activity, DateTimeOffset now,
        TimeSpan window, ulong destination, bool uncertain = false, Func<ulong, string?>? activityAtEnd = null, VoiceChannelRule? rule = null)
    {
        var session = Sessions.SingleOrDefault(x => x.UserId == user);
        if (session?.ConnectedSince != null && session.ChannelId == channel) return;
        if (session?.ConnectedSince != null)
        {
            var end = uncertain ? session.LastObserved : now;
            if (session.Rule?.TrackDuration != false) session.ConnectedSeconds += Math.Max(0, (end - session.ConnectedSince.Value).TotalSeconds);
            session.ConnectedSince = null;
            session.Disconnected = end;
            var label = Safe(activityAtEnd?.Invoke(session.ChannelId) ?? session.Activity);
            if (session.Rule?.LogDeparture != false) Pending.Add(new VoiceLog(session.Destination == 0 ? destination : session.Destination,
                $"<@{user}> left <#{session.ChannelId}> ({label})" +
                (session.Rule?.TrackDuration != false ? $"\nConnected for {Duration(session.ConnectedSeconds)}." : "") +
                (session.Rule?.TrackInterruptions != false ? $"\n{session.Interruptions} brief interruption{(session.Interruptions == 1 ? "" : "s")}." : "") +
                (uncertain ? "\nConnection during the observation gap is unknown; only confirmed time is counted." : ""), true));
        }
        if (channel == null || activity == null) return;
        if (session != null && session.ChannelId == channel && session.Rule?.TrackInterruptions != false && now - session.Disconnected <= window)
        {
            session.Interruptions++;
            session.ConnectedSince = now;
            session.LastObserved = now;
            if (session.Rule?.LogJoin != false) Join(session.Destination == 0 ? destination : session.Destination, user, channel.Value);
            session.Disconnected = null;
            return;
        }
        if (session != null) Sessions.Remove(session);
        Sessions.Add(new VoiceSession { UserId = user, ChannelId = channel.Value, Activity = activity, Destination = destination,
            Started = now, ConnectedSince = now, LastObserved = now, Rule = rule == null ? null : System.Text.Json.JsonSerializer.Deserialize<VoiceChannelRule>(System.Text.Json.JsonSerializer.Serialize(rule)) });
        if (rule?.LogJoin != false) Join(destination, user, channel.Value);
    }

    public void Checkpoint(DateTimeOffset now, TimeSpan window)
    {
        foreach (var session in Sessions.Where(x => x.ConnectedSince != null)) session.LastObserved = now;
        Sessions.RemoveAll(x => x.Disconnected != null && now - x.Disconnected > window);
    }

    private static string Safe(string value) => new(value.Where(c => !char.IsControl(c)).Take(100).Select(c => c is '`' or '@' or '*' or '_' or '~' or '<' or '>' ? ' ' : c).ToArray());
    internal static string Duration(double seconds)
    {
        var total = (long)Math.Max(0, seconds);
        var parts = new List<string>();
        void Part(long value, string unit) { if (value > 0) parts.Add($"{value} {unit}{(value == 1 ? "" : "s")}"); }
        Part(total / 3600, "hour"); Part(total / 60 % 60, "minute"); Part(total % 60, "second");
        return parts.Count == 0 ? "0 seconds" : string.Join(", ", parts);
    }
}
