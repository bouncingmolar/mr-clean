
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MrClean.Services;

public sealed class TimedMuteService : BackgroundService
{
    private const ulong TargetBotId = 716390085896962058;
    private const ulong MuteRoleId = 733993415733280909;
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(30);

    private static readonly Regex CommandPattern = new(
        @"^\s*(shush|unshush)\s+<@!?716390085896962058>\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DiscordSocketClient _client;
    private readonly ILogger<TimedMuteService> _logger;
    private readonly string _connectionString;
    private readonly ulong _guildId;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _databaseReady;

    public TimedMuteService(
        DiscordSocketClient client,
        IConfiguration configuration,
        ILogger<TimedMuteService> logger)
    {
        _client = client;
        _logger = logger;
        _guildId = configuration.GetValue<ulong>("Discord:GuildId");
        if (_guildId == 0)
            throw new InvalidOperationException("Discord guild ID is missing.");
        _connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Database connection is missing.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureDatabaseAsync(stoppingToken);
        _client.MessageReceived += HandleMessageAsync;

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessExpiredAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Timed mute expiry check failed");
                }

                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _client.MessageReceived -= HandleMessageAsync;
        }
    }

    private async Task EnsureDatabaseAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(@"CREATE TABLE IF NOT EXISTS ""TimedMutes"" (
                ""GuildId"" bigint NOT NULL,
                ""UserId"" bigint NOT NULL,
                ""RoleId"" bigint NOT NULL,
                ""ExpiresAt"" timestamptz NOT NULL,
                PRIMARY KEY (""GuildId"", ""UserId"", ""RoleId"")
            )", connection);

        await command.ExecuteNonQueryAsync(ct);
        _databaseReady = true;
        _logger.LogInformation("Timed mute database ready");
    }

    private async Task HandleMessageAsync(SocketMessage raw)
    {
        if (raw is not SocketUserMessage message ||
            message.Author.IsBot ||
            message.Channel is not SocketTextChannel channel)
            return;

        if (channel.Guild.Id != _guildId)
            return;

        var match = CommandPattern.Match(message.Content);
        if (!match.Success)
            return;

        if (!_databaseReady)
        {
            await message.ReplyAsync("The timed mute service is not ready.");
            return;
        }

        var moderator = channel.Guild.GetUser(message.Author.Id);
        if (moderator == null || !moderator.GuildPermissions.ManageRoles)
        {
            await message.ReplyAsync("You need Manage Roles permission to use this command.");
            return;
        }

        await _gate.WaitAsync();
        try
        {
            IGuildUser? target = channel.Guild.GetUser(TargetBotId);
            target ??= await _client.Rest.GetGuildUserAsync(channel.Guild.Id, TargetBotId);

            if (target == null)
            {
                await message.ReplyAsync("I cannot find that bot in this server.");
                return;
            }

            var role = channel.Guild.GetRole(MuteRoleId);
            if (role == null)
            {
                await message.ReplyAsync("The configured Mute role does not exist here.");
                return;
            }

            if (match.Groups[1].Value.Equals("shush", StringComparison.OrdinalIgnoreCase))
            {
                await ShushAsync(message, target, role);
            }
            else
            {
                await UnshushAsync(message, target, role);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Timed mute command failed");
            await message.ReplyAsync("The command failed. Check Mr-Clean's permissions and logs.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ShushAsync(SocketUserMessage message, IGuildUser target, SocketRole role)
    {
        var guildId = target.Guild.Id;
        var existing = await GetExpiryAsync(guildId);

        // Fetch current membership rather than trusting the socket cache.
        var current = await _client.Rest.GetGuildUserAsync(guildId, TargetBotId);
        if (current == null)
            throw new InvalidOperationException("Target bot is no longer in the server.");

        var alreadyMuted = current.RoleIds.Contains(MuteRoleId);

        if (existing == null && alreadyMuted)
        {
            await message.ReplyAsync(
                "That bot already has the Mute role. I won't take ownership of an existing mute.");
            return;
        }

        var expiry = DateTime.UtcNow.Add(Duration);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var command = new NpgsqlCommand(
            @"INSERT INTO ""TimedMutes"" (""GuildId"", ""UserId"", ""RoleId"", ""ExpiresAt"")
              VALUES (@guild, @user, @role, @expiry)
              ON CONFLICT (""GuildId"", ""UserId"", ""RoleId"")
              DO UPDATE SET ""ExpiresAt"" = EXCLUDED.""ExpiresAt""",
            connection, transaction))
        {
            command.Parameters.AddWithValue("guild", checked((long)guildId));
            command.Parameters.AddWithValue("user", checked((long)TargetBotId));
            command.Parameters.AddWithValue("role", checked((long)MuteRoleId));
            command.Parameters.AddWithValue("expiry", expiry);
            await command.ExecuteNonQueryAsync();
        }

        // Persist the timer before assigning the Discord role.
        await transaction.CommitAsync();

        if (!alreadyMuted)
        {
            try
            {
                await target.AddRoleAsync(role);
            }
            catch
            {
                // Only remove a newly created timer. Preserve existing timers.
                if (existing == null)
                    await DeleteMuteAsync(guildId);

                throw;
            }
        }

        await message.ReplyAsync(
            "<@716390085896962058> go bye bye for 30 minutes.",
            allowedMentions: AllowedMentions.None);
    }

    private async Task UnshushAsync(SocketUserMessage message, IGuildUser target, SocketRole role)
    {
        if (await GetExpiryAsync(target.Guild.Id) == null)
        {
            await message.ReplyAsync("There is no temporary shush active for that bot.");
            return;
        }

        var current = await _client.Rest.GetGuildUserAsync(target.Guild.Id, TargetBotId);
        if (current != null && current.RoleIds.Contains(MuteRoleId))
            await current.RemoveRoleAsync(role);

        await DeleteMuteAsync(target.Guild.Id);
        await message.ReplyAsync("Unshushed.");
    }

    private async Task<DateTime?> GetExpiryAsync(ulong guildId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"SELECT ""ExpiresAt"" FROM ""TimedMutes""
            WHERE ""GuildId"" = @guild AND ""UserId"" = @user AND ""RoleId"" = @role", connection);

        command.Parameters.AddWithValue("guild", checked((long)guildId));
        command.Parameters.AddWithValue("user", checked((long)TargetBotId));
        command.Parameters.AddWithValue("role", checked((long)MuteRoleId));

        var result = await command.ExecuteScalarAsync();
        return result is DateTime expiry ? expiry : null;
    }

    private async Task DeleteMuteAsync(ulong guildId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"DELETE FROM ""TimedMutes""
            WHERE ""GuildId"" = @guild AND ""UserId"" = @user AND ""RoleId"" = @role", connection);

        command.Parameters.AddWithValue("guild", checked((long)guildId));
        command.Parameters.AddWithValue("user", checked((long)TargetBotId));
        command.Parameters.AddWithValue("role", checked((long)MuteRoleId));

        await command.ExecuteNonQueryAsync();
    }

    private async Task ProcessExpiredAsync(CancellationToken ct)
    {
        if (_client.ConnectionState != ConnectionState.Connected)
            return;

        await _gate.WaitAsync(ct);
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var command = new NpgsqlCommand(@"SELECT ""GuildId"" FROM ""TimedMutes""
                WHERE ""UserId"" = @user AND ""RoleId"" = @role
                  AND ""ExpiresAt"" <= @now", connection);

            command.Parameters.AddWithValue("user", checked((long)TargetBotId));
            command.Parameters.AddWithValue("role", checked((long)MuteRoleId));
            command.Parameters.AddWithValue("now", DateTime.UtcNow);

            var guildIds = new List<ulong>();
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    guildIds.Add(checked((ulong)reader.GetInt64(0)));
            }

            foreach (var guildId in guildIds)
            {
                try
                {
                    var guild = _client.GetGuild(guildId);
                    if (guild == null)
                        continue;

                    IGuildUser? target = await _client.Rest.GetGuildUserAsync(guild.Id, TargetBotId);

                    if (target == null)
                        continue;

                    var role = guild.GetRole(MuteRoleId);
                    if (role == null)
                        continue;

                    if (target.RoleIds.Contains(MuteRoleId))
                        await target.RemoveRoleAsync(role);

                    await DeleteMuteAsync(guildId);
                    _logger.LogInformation("Expired timed mute in guild {GuildId}", guildId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not expire mute in guild {GuildId}", guildId);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
