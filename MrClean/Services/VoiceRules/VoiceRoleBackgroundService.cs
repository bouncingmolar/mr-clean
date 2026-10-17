using System.Collections.Concurrent;
using MrClean.Services.Activities;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using MrClean.Configuration;

namespace MrClean.Services.VoiceRules;

public class VoiceRoleBackgroundService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly VoiceRoleReconciler _reconciler;
    private readonly DiscordOptions _discord;
    private readonly VoiceLoggingOptions _options;
    private readonly ILogger<VoiceRoleBackgroundService> _logger;
    private readonly PresenceSnapshotCache _presence;
    private readonly ActivityRulesService _activities;
    private readonly ActivityOptions _activityOptions;
    private readonly ConcurrentDictionary<ulong, byte> _dirty = new();
    private int _fullSweep = 1;
    private bool Active => _options.Enabled || _activityOptions.ObservePresences;
    private readonly SemaphoreSlim _wake = new(0, 1);
    public VoiceRoleBackgroundService(DiscordSocketClient client, VoiceRoleReconciler reconciler,
        IOptions<DiscordOptions> discord, IOptions<VoiceLoggingOptions> options, ILogger<VoiceRoleBackgroundService> logger,
        PresenceSnapshotCache presence, ActivityRulesService activities, IOptions<ActivityOptions> activityOptions)
    { _client = client; _reconciler = reconciler; _discord = discord.Value; _options = options.Value; _logger = logger; _presence = presence; _activities = activities; _activityOptions = activityOptions.Value; }
    public override Task StartAsync(CancellationToken token)
    {
        if (Active)
        {
            if (_activityOptions.ReconcileSeconds < 10 || _activityOptions.ReconcileSeconds > 300) throw new InvalidOperationException("Activity reconcile interval must be 10–300 seconds.");
            _presence.Changed += PresenceChanged;
            _activities.Changed += RulesChanged;
            _client.UserVoiceStateUpdated += VoiceAsync;
            _client.Ready += ReadyAsync;
            _client.GuildAvailable += AvailableAsync;
        }
        return base.StartAsync(token);
    }
    private void PresenceChanged(ulong guild, ulong user)
    { if (guild == _discord.GuildId) { _dirty[user] = 0; _ = WakeAsync(); } }
    private void RulesChanged(ulong guild) { if (guild == _discord.GuildId) _ = ReadyAsync(); }
    private Task ReadyAsync() { Interlocked.Exchange(ref _fullSweep, 1); return WakeAsync(); }
    private Task WakeAsync() { try { _wake.Release(); } catch (SemaphoreFullException) { } return Task.CompletedTask; }
    private Task AvailableAsync(SocketGuild guild) => guild.Id == _discord.GuildId ? ReadyAsync() : Task.CompletedTask;
    private Task VoiceAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after) =>
        CaptureVoice(user, before, after);
    private Task CaptureVoice(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        if (!user.IsBot && (after.VoiceChannel?.Guild.Id ?? before.VoiceChannel?.Guild.Id) == _discord.GuildId)
        { _dirty[user.Id] = 0; return WakeAsync(); }
        return Task.CompletedTask;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Active) return;
        var nextSweep = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var signaled = await _wake.WaitAsync(TimeSpan.FromSeconds(_activityOptions.ReconcileSeconds), stoppingToken);
            var guild = _client.GetGuild(_discord.GuildId);
            if (guild?.IsConnected != true || _client.ConnectionState != ConnectionState.Connected) continue;
            try
            {
                var dirty = _dirty.Keys.ToArray();
                foreach (var user in dirty) _dirty.TryRemove(user, out _);
                var full = !signaled || DateTimeOffset.UtcNow >= nextSweep || Interlocked.Exchange(ref _fullSweep, 0) == 1;
                if (full)
                {
                    nextSweep = DateTimeOffset.UtcNow.AddSeconds(_activityOptions.ReconcileSeconds); Interlocked.Exchange(ref _fullSweep, 0);
                    if (_activityOptions.ObservePresences)
                        foreach (var user in guild.Users.Where(x => !x.IsBot && x.Activities.Count > 0))
                            if (_presence.Get(guild.Id, user.Id).Revision == 0)
                                _presence.SeedPositive(guild.Id, user.Id, PresenceObserver.Copy(user.Activities));
                }
                var users = full ? dirty.Concat(_presence.Users(guild.Id)).Concat(guild.VoiceChannels.SelectMany(x => x.ConnectedUsers).Where(x => !x.IsBot).Select(x => x.Id)).Distinct().ToArray() : dirty;
                await _reconciler.ReconcileAsync(guild.Id, users, stoppingToken, full);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { _logger.LogWarning(ex, "Voice role reconciliation unavailable; retrying."); }
        }
    }
    public override Task StopAsync(CancellationToken token)
    {
        _presence.Changed -= PresenceChanged; _activities.Changed -= RulesChanged;
        _client.UserVoiceStateUpdated -= VoiceAsync;
        _client.Ready -= ReadyAsync;
        _client.GuildAvailable -= AvailableAsync;
        return base.StopAsync(token);
    }
}
