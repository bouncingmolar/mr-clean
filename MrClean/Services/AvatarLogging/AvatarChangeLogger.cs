using System.Net.Http;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MrClean.Configuration;

namespace MrClean.Services.AvatarLogging;

public sealed class AvatarChangeLogger : BackgroundService
{
    private const ulong AvatarLoggingChannelId = 907221623172640838;
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromHours(6);
    private static readonly HttpClient HttpClient = new();

    private readonly DiscordSocketClient _client;
    private readonly ILogger<AvatarChangeLogger> _logger;
    private readonly ulong _guildId;
    private readonly AvatarSnapshotCache _avatars = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _refresh = new(0, 1);
    private volatile bool _initialized;

    public AvatarChangeLogger(
        DiscordSocketClient client,
        IOptions<DiscordOptions> options,
        ILogger<AvatarChangeLogger> logger)
    {
        _client = client;
        _guildId = options.Value.GuildId;
        _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe synchronously before the host starts the Discord connection service.
        _client.Ready += HandleReadyAsync;
        _client.GuildAvailable += HandleGuildAvailableAsync;
        _client.GuildMemberUpdated += HandleGuildMemberUpdatedAsync;
        _client.UserUpdated += HandleUserUpdatedAsync;
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _refresh.WaitAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await ReconcileAsync(stoppingToken);
                await _refresh.WaitAsync(
                    _initialized ? ReconciliationInterval : TimeSpan.FromSeconds(30),
                    stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _client.Ready -= HandleReadyAsync;
            _client.GuildAvailable -= HandleGuildAvailableAsync;
            _client.GuildMemberUpdated -= HandleGuildMemberUpdatedAsync;
            _client.UserUpdated -= HandleUserUpdatedAsync;
        }
    }

    private Task HandleReadyAsync()
    {
        // Never await a member download on the gateway event loop: it must process
        // member chunks to complete that download.
        lock (_refresh)
        {
            if (_refresh.CurrentCount == 0)
                _refresh.Release();
        }
        return Task.CompletedTask;
    }

    private Task HandleGuildAvailableAsync(SocketGuild guild)
    {
        return guild.Id == _guildId ? HandleReadyAsync() : Task.CompletedTask;
    }

    private async Task HandleGuildMemberUpdatedAsync(
        Cacheable<SocketGuildUser, ulong> before,
        SocketGuildUser after)
    {
        if (after.Guild.Id != _guildId || !_initialized)
            return;

        await ProcessAvatarAsync(
            after,
            after.DisplayAvatarId,
            after.GetDisplayAvatarUrl());
    }

    private async Task HandleUserUpdatedAsync(SocketUser before, SocketUser after)
    {
        if (!_initialized)
            return;

        var guild = _client.GetGuild(_guildId);
        var member = guild?.GetUser(after.Id);
        if (member == null)
            return;

        var effectiveAvatarId = AvatarSnapshot.GetEffectiveAvatarId(
            member.GuildAvatarId,
            after.AvatarId);
        var avatarUrl = member.GuildAvatarId == null
            ? after.GetAvatarUrl() ?? member.GetDisplayAvatarUrl()
            : member.GetDisplayAvatarUrl();

        await ProcessAvatarAsync(member, effectiveAvatarId, avatarUrl);
    }

    private async Task ProcessAvatarAsync(
        SocketGuildUser member,
        string? effectiveAvatarId,
        string? avatarUrl)
    {
        if (!_initialized)
            return;

        await _gate.WaitAsync();
        try
        {
            if (!_initialized)
                return;

            if (!_avatars.TryGet(member.Id, out _))
            {
                _avatars.Remember(member.Id, effectiveAvatarId);
                return;
            }

            if (!_avatars.HasChanged(member.Id, effectiveAvatarId))
                return;

            await SendAvatarChangeAsync(member, avatarUrl);
            _avatars.Remember(member.Id, effectiveAvatarId);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to log avatar change for user {UserId}.",
                member.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SendAvatarChangeAsync(SocketGuildUser member, string? avatarUrl)
    {
        var channel = _client.GetChannel(AvatarLoggingChannelId) as SocketTextChannel;
        if (channel == null || channel.Guild.Id != _guildId)
        {
            _logger.LogWarning(
                "Avatar logging channel {ChannelId} was not found in guild {GuildId}.",
                AvatarLoggingChannelId,
                _guildId);
            throw new InvalidOperationException(
                $"Avatar logging channel {AvatarLoggingChannelId} is unavailable.");
        }

        if (string.IsNullOrWhiteSpace(avatarUrl) ||
            !Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"No valid display avatar URL is available for user {member.Id}.");
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (extension is not (".png" or ".gif" or ".webp" or ".jpg" or ".jpeg"))
            extension = ".png";

        var fileName = $"avatar-{member.Id}{extension}";
        var avatar = await HttpClient.GetByteArrayAsync(uri);
        await using var stream = new MemoryStream(avatar);

        var embed = new EmbedBuilder()
            .WithColor(new Color(0x5865F2))
            .WithTitle("Avatar Change")
            .WithAuthor(member.Username)
            .WithDescription($"<@{member.Id}> changed their avatar.\nNew image below")
            .WithFooter($"ID: {member.Id}")
            .WithCurrentTimestamp()
            .WithImageUrl($"attachment://{fileName}")
            .Build();

        await channel.SendFileAsync(stream, fileName, embed: embed);
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var guild = _client.GetGuild(_guildId);
        if (guild == null)
        {
            _logger.LogWarning(
                "Cannot reconcile avatars because guild {GuildId} is unavailable.",
                _guildId);
            return;
        }

        try
        {
            await _avatars.InitializeAsync(
                () => guild.DownloadUsersAsync().WaitAsync(cancellationToken),
                () => guild.Users.Select(member =>
                    new KeyValuePair<ulong, string?>(member.Id, member.DisplayAvatarId)),
                cancellationToken);

            if (!_initialized)
            {
                _initialized = true;
                _logger.LogInformation(
                    "Initialized avatar snapshots for {MemberCount} members in guild {GuildId}.",
                    guild.Users.Count,
                    _guildId);
                return;
            }

            await ReconcileGuildAsync(guild, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Avatar reconciliation failed for guild {GuildId}.",
                _guildId);
        }
    }

    private async Task ReconcileGuildAsync(
        SocketGuild guild,
        CancellationToken cancellationToken)
    {
        foreach (var member in guild.Users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessAvatarAsync(
                member,
                member.DisplayAvatarId,
                member.GetDisplayAvatarUrl());
        }
    }
}
