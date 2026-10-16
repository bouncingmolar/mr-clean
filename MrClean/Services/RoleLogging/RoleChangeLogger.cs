using Discord;
using Discord.WebSocket;

namespace MrClean.Services.RoleLogging;

public class RoleChangeLogger
{
    private readonly IRoleLoggingService _settings;
    private readonly ILogger<RoleChangeLogger> _logger;

    public RoleChangeLogger(
        IRoleLoggingService settings,
        ILogger<RoleChangeLogger> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public void Register(DiscordSocketClient client)
    {
        client.GuildMemberUpdated += HandleAsync;
    }

    private async Task HandleAsync(
        Cacheable<SocketGuildUser, ulong> beforeCache,
        SocketGuildUser after)
    {
        try
        {
            var before = await beforeCache.GetOrDownloadAsync();

            if (before == null)
                return;

            var beforeIds = before.Roles.Select(x => x.Id).ToHashSet();
            var afterIds = after.Roles.Select(x => x.Id).ToHashSet();

            var added = after.Roles
                .Where(x => !beforeIds.Contains(x.Id))
                .ToList();

            var removed = before.Roles
                .Where(x => !afterIds.Contains(x.Id))
                .ToList();

            if (added.Count == 0 && removed.Count == 0)
                return;

            var config = await _settings.GetAsync();

            if (config == null || !config.Enabled)
                return;

            var changes = added
                .Select(role => (Role: role, Added: true))
                .Concat(
                    removed.Select(role => (Role: role, Added: false)))
                .ToList();

            // Group changes by their configured destination.
            // Unrouted roles go to the default role-log channel.
            var groups = changes.GroupBy(change =>
                config.Routes
                    .FirstOrDefault(x => x.RoleId == change.Role.Id)
                    ?.ChannelId
                ?? config.DefaultChannelId);

            foreach (var group in groups)
            {
                var channel = after.Guild.GetTextChannel(group.Key);

                if (channel == null)
                {
                    _logger.LogWarning(
                        "Role logging channel {ChannelId} was not found.",
                        group.Key);
                    continue;
                }

                var groupAdded = group
                    .Where(x => x.Added)
                    .Select(x => x.Role)
                    .ToList();

                var groupRemoved = group
                    .Where(x => !x.Added)
                    .Select(x => x.Role)
                    .ToList();

                var embed = new EmbedBuilder()
                    .WithColor(new Color(0x5865F2))
                    .WithAuthor(
                        after.DisplayName,
                        after.GetDisplayAvatarUrl())
                    .WithDescription($"<@{after.Id}> had their roles changed.")
                    .WithFooter($"ID: {after.Id}")
                    .WithCurrentTimestamp();

                if (groupAdded.Count > 0)
                    embed.AddField(
                        "Given",
                        string.Join(
                            "\n",
                            groupAdded.Select(x => $"`{x.Name}`")));

                if (groupRemoved.Count > 0)
                    embed.AddField(
                        "Removed",
                        string.Join(
                            "\n",
                            groupRemoved.Select(x => $"`{x.Name}`")));

                await channel.SendMessageAsync(embed: embed.Build());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log role change.");
        }
    }
}
