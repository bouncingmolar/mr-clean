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
    private readonly SemaphoreSlim _wake = new(0, 1);
    public VoiceRoleBackgroundService(DiscordSocketClient client, VoiceRoleReconciler reconciler,
        IOptions<DiscordOptions> discord, IOptions<VoiceLoggingOptions> options, ILogger<VoiceRoleBackgroundService> logger)
    { _client = client; _reconciler = reconciler; _discord = discord.Value; _options = options.Value; _logger = logger; }
    public override Task StartAsync(CancellationToken token)
    {
        if (_options.Enabled)
        {
            _client.UserVoiceStateUpdated += VoiceAsync;
            _client.Ready += ReadyAsync;
            _client.GuildAvailable += AvailableAsync;
        }
        return base.StartAsync(token);
    }
    private Task ReadyAsync() { try { _wake.Release(); } catch (SemaphoreFullException) { } return Task.CompletedTask; }
    private Task AvailableAsync(SocketGuild guild) => guild.Id == _discord.GuildId ? ReadyAsync() : Task.CompletedTask;
    private Task VoiceAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after) =>
        !user.IsBot && (after.VoiceChannel?.Guild.Id ?? before.VoiceChannel?.Guild.Id) == _discord.GuildId
            ? ReadyAsync() : Task.CompletedTask;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            await _wake.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken);
            var guild = _client.GetGuild(_discord.GuildId);
            if (guild?.IsConnected != true || _client.ConnectionState != ConnectionState.Connected) continue;
            try
            {
                await _reconciler.ReconcileAsync(guild.Id,
                    guild.VoiceChannels.SelectMany(x => x.ConnectedUsers).Where(x => !x.IsBot).Select(x => x.Id).ToArray(), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { _logger.LogWarning(ex, "Voice role reconciliation unavailable; retrying."); }
        }
    }
    public override Task StopAsync(CancellationToken token)
    {
        _client.UserVoiceStateUpdated -= VoiceAsync;
        _client.Ready -= ReadyAsync;
        _client.GuildAvailable -= AvailableAsync;
        return base.StopAsync(token);
    }
}
