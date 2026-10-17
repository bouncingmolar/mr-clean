using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MrClean.Configuration;
using MrClean.Data;
using MrClean.Models;

namespace MrClean.Services.VoiceLogging;

public class VoiceActivityLogger : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly IDbContextFactory<MrCleanDbContext> _factory;
    private readonly VoiceLoggingOptions _options;
    private readonly ulong _guildId;
    private readonly ILogger<VoiceActivityLogger> _logger;
    private readonly VoiceEventPump _pump = new();
    private readonly object _captureLock = new();
    private readonly SemaphoreSlim _deliveryWake = new(0, 1);
    private bool _observing;
    private bool _stopping;
    private Task? _processing;
    private TimeSpan Window => TimeSpan.FromSeconds(_options.ReconnectWindowSeconds);

    public VoiceActivityLogger(DiscordSocketClient client, IDbContextFactory<MrCleanDbContext> factory,
        IOptions<VoiceLoggingOptions> options, IOptions<DiscordOptions> discord, ILogger<VoiceActivityLogger> logger)
    {
        _client = client; _factory = factory; _options = options.Value;
        _guildId = discord.Value.GuildId; _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Enabled)
        {
            if (_options.LogChannelId == 0 || _options.ReconnectWindowSeconds < 0 ||
                _options.Channels.Any(x => x.Key == 0 || string.IsNullOrWhiteSpace(x.Value)) ||
                _options.ChannelLogDestinations.Any(x => x.Key == 0 || x.Value == 0))
                throw new InvalidOperationException("Invalid VoiceLogging configuration.");
            _client.Ready += ReadyAsync;
            _client.GuildAvailable += GuildAvailableAsync;
            _client.Disconnected += DisconnectedAsync;
            _client.GuildUnavailable += GuildUnavailableAsync;
            _client.UserVoiceStateUpdated += VoiceAsync;
        }
        return base.StartAsync(cancellationToken);
    }

    private Task ReadyAsync() { CaptureSnapshot(); return Task.CompletedTask; }
    private Task GuildAvailableAsync(SocketGuild guild) => guild.Id == _guildId ? ReadyAsync() : Task.CompletedTask;
    private Task DisconnectedAsync(Exception exception) { lock (_captureLock) _observing = false; return Task.CompletedTask; }
    private Task GuildUnavailableAsync(SocketGuild guild)
    {
        if (guild.Id == _guildId) { lock (_captureLock) _observing = false; }
        return Task.CompletedTask;
    }

    private Task VoiceAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        // No database or REST work, and no awaits, on the gateway dispatch path.
        lock (_captureLock)
        {
            if (_stopping || !_observing || user.IsBot || before.VoiceChannel?.Id == after.VoiceChannel?.Id ||
                (after.VoiceChannel?.Guild.Id ?? before.VoiceChannel?.Guild.Id) != _guildId)
                return Task.CompletedTask;
            var at = DateTimeOffset.UtcNow;
            var userId = user.Id;
            var channel = after.VoiceChannel;
            var activity = channel == null ? null : _options.Activity(channel.Id, channel.Name);
            ulong? channelId = activity == null ? null : channel!.Id;
            var destination = _options.Destination(channelId ?? before.VoiceChannel?.Id ?? 0);
            var departingId = before.VoiceChannel?.Id;
            var departingName = departingId == _options.GeneralChannelId ? before.VoiceChannel?.Name : null;
            _pump.Capture(state => state.Transition(userId, channelId, activity, at, Window, destination,
                activityAtEnd: id => id == departingId ? departingName : null));
        }
        return Task.CompletedTask;
    }

    private void CaptureSnapshot()
    {
        lock (_captureLock)
        {
            var guild = _client.GetGuild(_guildId);
            if (_stopping || _observing || guild?.IsConnected != true || _client.ConnectionState != ConnectionState.Connected) return;
            var at = DateTimeOffset.UtcNow;
            var names = guild.VoiceChannels.ToDictionary(x => x.Id, x => x.Name);
            var occupants = guild.VoiceChannels.SelectMany(channel => channel.ConnectedUsers
                .Where(user => !user.IsBot).Select(user => (User: user.Id, Channel: channel.Id, Name: channel.Name))).ToArray();
            _pump.Capture(state =>
            {
                foreach (var session in state.Sessions.Where(x => x.ConnectedSince != null).ToList())
                    state.Transition(session.UserId, null, null, at, Window, _options.Destination(session.ChannelId),
                        uncertain: true, activityAtEnd: id => id == _options.GeneralChannelId ? names.GetValueOrDefault(id) : null);
                foreach (var occupant in occupants)
                {
                    var activity = _options.Activity(occupant.Channel, occupant.Name);
                    if (activity != null) state.Transition(occupant.User, occupant.Channel, activity, at, Window,
                        _options.Destination(occupant.Channel));
                }
                state.Checkpoint(at, Window);
            });
            _observing = true;
        }
    }

    private async Task PersistAsync(VoiceActivityTracker state, CancellationToken token)
    {
        // Stable row instances/IDs survive uncertain commits and retries.
        var commit = VoiceStateCommit.Prepare(state);
        while (true)
        {
            try
            {
                await using var db = await _factory.CreateDbContextAsync(token);
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                var row = await db.VoiceActivityStates.FindAsync(new object[] { 1 }, token);
                // An uncertain previous commit may already contain this exact state.
                if (row?.Json != commit.Json)
                {
                    if (row == null) { row = new VoiceActivityState { Id = 1 }; db.Add(row); }
                    row.Json = commit.Json;
                    db.VoiceLogMessages.AddRange(commit.Logs);
                    await db.SaveChangesAsync(token);
                }
                await transaction.CommitAsync(token);
                WakeDelivery();
                return;
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                _logger.LogError(ex, "Voice state commit failed; retaining captured events and retrying.");
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
        }
    }

    private async Task ObserveAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(token))
        {
            CaptureSnapshot(); // Also handles gateway resumes without a new Ready event.
            lock (_captureLock)
            {
                if (_stopping || !_observing || _client.ConnectionState != ConnectionState.Connected ||
                    _client.GetGuild(_guildId)?.IsConnected != true) continue;
                var at = DateTimeOffset.UtcNow;
                _pump.Capture(state => state.Checkpoint(at, Window));
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        VoiceActivityTracker? state = null;
        while (state == null)
        {
            try
            {
                await using var db = await _factory.CreateDbContextAsync(stoppingToken);
                var row = await db.VoiceActivityStates.FindAsync(new object[] { 1 }, stoppingToken);
                state = row == null ? new VoiceActivityTracker() : JsonSerializer.Deserialize<VoiceActivityTracker>(row.Json)
                    ?? throw new InvalidOperationException("Invalid persisted voice state.");
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Voice state load failed; retrying after database startup.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
        _processing = _pump.RunAsync(state, () => PersistAsync(state, stoppingToken), stoppingToken);
        await Task.WhenAll(_processing, ObserveAsync(stoppingToken), DeliverAsync(stoppingToken));
    }

    private async Task DeliverAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var db = await _factory.CreateDbContextAsync(token);
                var heads = await db.VoiceLogMessages.GroupBy(x => x.Destination)
                    .Select(x => x.Min(y => y.Id)).ToListAsync(token);
                foreach (var id in heads.OrderBy(x => x))
                {
                    // A failed destination must not block other destinations.
                    await DeliverOneAsync(id, token);
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                _logger.LogError(ex, "Voice outbox unavailable; retrying.");
            }
            await _deliveryWake.WaitAsync(TimeSpan.FromSeconds(1), token);
        }
    }

    private void WakeDelivery()
    {
        try { _deliveryWake.Release(); }
        catch (SemaphoreFullException) { /* A wake-up is already pending. */ }
    }

    private async Task DeliverOneAsync(long id, CancellationToken token)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync(token);
            var log = await db.VoiceLogMessages.FindAsync(new object[] { id }, token);
            if (log == null) return;
            var channel = _client.GetGuild(_guildId)?.GetTextChannel(log.Destination)
                ?? throw new InvalidOperationException($"Voice log channel {log.Destination} unavailable.");
            await VoiceLogDelivery.DeliverAsync(log,
                async () =>
                {
                    var recent = await channel.GetMessagesAsync(100, options: new RequestOptions { CancelToken = token }).FlattenAsync();
                    return recent.Any(x => x.Author.Id == _client.CurrentUser.Id && x.Content == log.Text &&
                        x.CreatedAt >= log.AttemptedAt!.Value.AddSeconds(-1));
                },
                () => db.SaveChangesAsync(token),
                async () => { await channel.SendMessageAsync(log.Text, allowedMentions: AllowedMentions.None,
                    options: new RequestOptions { CancelToken = token }); },
                async () => { db.Remove(log); await db.SaveChangesAsync(token); WakeDelivery(); });
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            _logger.LogError(ex, "Voice log {Id} delivery failed; retrying destination in order.", id);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _client.Ready -= ReadyAsync; _client.GuildAvailable -= GuildAvailableAsync;
        _client.Disconnected -= DisconnectedAsync; _client.GuildUnavailable -= GuildUnavailableAsync;
        _client.UserVoiceStateUpdated -= VoiceAsync;
        lock (_captureLock) { _stopping = true; _pump.Complete(); }
        try
        {
            // Commit captured observations before a normal shutdown; unsent outbox rows survive restart.
            if (_processing != null) await _processing.WaitAsync(cancellationToken);
        }
        finally { await base.StopAsync(cancellationToken); }
    }
}
