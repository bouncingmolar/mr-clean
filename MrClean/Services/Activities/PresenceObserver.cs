using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using MrClean.Configuration;

namespace MrClean.Services.Activities;

// Receives snapshots only; role API operations remain in the combined reconciler.
public class PresenceObserver : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly PresenceSnapshotCache _cache;
    private readonly ulong _guild;
    private readonly bool _enabled;
    public PresenceObserver(DiscordSocketClient client, PresenceSnapshotCache cache,
        IOptions<DiscordOptions> discord, IOptions<ActivityOptions> options)
    { _client = client; _cache = cache; _guild = discord.Value.GuildId; _enabled = options.Value.ObservePresences; }
    public Task StartAsync(CancellationToken token)
    {
        if (_enabled)
        {
            _client.PresenceUpdated += UpdatedAsync; _client.Disconnected += DisconnectedAsync;
            _client.GuildUnavailable += UnavailableAsync; _client.Ready += ReadyAsync;
            _client.UserLeft += LeftAsync;
        }
        return Task.CompletedTask;
    }
    private Task UpdatedAsync(SocketUser user, SocketPresence before, SocketPresence after)
    {
        if (user is SocketGuildUser source && source.Guild.Id != _guild) return Task.CompletedTask;
        var guild = _client.GetGuild(_guild); var current = guild?.GetUser(user.Id);
        if (user.IsBot || current == null || guild?.IsConnected != true || _client.ConnectionState != ConnectionState.Connected) return Task.CompletedTask;
        var observed = Copy(after.Activities);
        if (!observed.SequenceEqual(Copy(current.Activities))) return Task.CompletedTask; // Ignore delayed notifications superseded in the SDK cache.
        // Offline and an empty client_status are not connection failures. A fresh event's
        // empty activities list authoritatively ends activities, regardless of online status.
        CaptureUpdate(_cache, _guild, user.Id, after);
        return Task.CompletedTask;
    }
    // Kept separate from cache seeding so cached empty presence cannot accidentally clear roles.
    public static void CaptureUpdate(PresenceSnapshotCache cache, ulong guild, ulong user, IPresence after)
        => cache.ObserveGateway(guild, user, Copy(after.Activities));
    public static ReportedActivity[] Copy(IEnumerable<IActivity> values) => values.Select(x =>
        new ReportedActivity(x.Name, x.Type, x is RichGame rich ? rich.ApplicationId : null)).ToArray();
    private Task DisconnectedAsync(Exception ex) { _cache.Invalidate(_guild); return Task.CompletedTask; }
    private Task UnavailableAsync(SocketGuild guild) => guild.Id == _guild ? DisconnectedAsync(null!) : Task.CompletedTask;
    private Task LeftAsync(SocketGuild guild, SocketUser user)
    { if (guild.Id == _guild) _cache.Forget(_guild, user.Id); return Task.CompletedTask; }
    private Task ReadyAsync()
    {
        // Positive cached activities can seed startup. Empty cache entries never establish inactivity.
        var guild = _client.GetGuild(_guild);
        if (guild != null) foreach (var user in guild.Users.Where(x => !x.IsBot && x.Activities.Count > 0))
            if (!_cache.Get(_guild, user.Id).Known) _cache.SeedPositive(_guild, user.Id, Copy(user.Activities));
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken token)
    {
        _client.PresenceUpdated -= UpdatedAsync; _client.Disconnected -= DisconnectedAsync;
        _client.GuildUnavailable -= UnavailableAsync; _client.Ready -= ReadyAsync; _client.UserLeft -= LeftAsync;
        return Task.CompletedTask;
    }
}
