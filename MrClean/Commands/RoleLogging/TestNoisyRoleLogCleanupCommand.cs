using Discord;
using Discord.WebSocket;
using MrClean.Services;
using MrClean.Services.RoleLogging;

namespace MrClean.Commands.RoleLogging;

public class TestNoisyRoleLogCleanupCommand : ISlashCommandProvider
{
    private const ulong SourceChannelId = 715471424223182939;
    private const ulong NoiseChannelId = 1556935145418919937;
    private const int PageSize = 100;

    private readonly DiscordSocketClient _client;
    private readonly IRoleLoggingService _roleLogging;

    public TestNoisyRoleLogCleanupCommand(
        DiscordSocketClient client,
        IRoleLoggingService roleLogging)
    {
        _client = client;
        _roleLogging = roleLogging;
    }

    public ApplicationCommandProperties Properties { get; } =
        new SlashCommandBuilder()
            .WithName("test-noisy-role-cleanup")
            .WithDescription("Dry-runs cleanup of old noisy Dyno role logs.")
            .Build();

    public async Task HandleCommandInvocationAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var channel = _client.GetChannel(SourceChannelId) as IMessageChannel;

        if (channel == null)
        {
            await command.FollowupAsync(
                $"Could not access <#{SourceChannelId}>.");
            return;
        }

        var config = await _roleLogging.GetAsync();

        if (config == null)
        {
            await command.FollowupAsync("Role logging is not configured.");
            return;
        }

        var noisyRoleIds = config.Routes
            .Where(x => x.ChannelId == NoiseChannelId)
            .Select(x => x.RoleId)
            .ToHashSet();

        if (noisyRoleIds.Count == 0)
        {
            await command.FollowupAsync(
                $"No roles are currently routed to <#{NoiseChannelId}>.");
            return;
        }

        var guild = (channel as SocketGuildChannel)?.Guild;

        if (guild == null)
        {
            await command.FollowupAsync("Source is not a guild channel.");
            return;
        }

        var noisyRoleNames = noisyRoleIds
            .Select(id => guild.GetRole(id))
            .Where(role => role != null)
            .Select(role => role!.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scanned = 0;
        var webhookMessages = 0;
        var matches = new List<IMessage>();
        var webhookAuthors = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

        ulong? beforeMessageId = null;

        while (true)
        {
            IEnumerable<IMessage> messages;

            if (beforeMessageId.HasValue)
            {
                messages = await channel.GetMessagesAsync(
                        beforeMessageId.Value,
                        Direction.Before,
                        PageSize,
                        CacheMode.AllowDownload)
                    .FlattenAsync();
            }
            else
            {
                messages = await channel.GetMessagesAsync(
                        PageSize,
                        CacheMode.AllowDownload)
                    .FlattenAsync();
            }

            var page = messages.ToList();

            if (page.Count == 0)
                break;

            var ordered = page
                .OrderByDescending(x => x.Timestamp)
                .ToList();

            foreach (var message in ordered)
            {
                scanned++;

                if (!message.Author.IsWebhook)
                    continue;

                webhookMessages++;

                var author = message.Author.Username;

                webhookAuthors[author] =
                    webhookAuthors.TryGetValue(author, out var count)
                        ? count + 1
                        : 1;

                var texts = MessageTextExtractor.GetTexts(message).ToList();

                var matchesNoisyRole = noisyRoleNames.Any(roleName =>
                    texts.Any(text =>
                        text.Contains(
                            $"`{roleName}`",
                            StringComparison.OrdinalIgnoreCase)));

                if (matchesNoisyRole)
                    matches.Add(message);
            }

            if (page.Count < PageSize)
                break;

            beforeMessageId = ordered[^1].Id;

            if (scanned % 500 == 0)
                Console.WriteLine(
                    $"Role cleanup dry run: {scanned} scanned, " +
                    $"{matches.Count} candidates");
        }

        var authors = string.Join(
            "\n",
            webhookAuthors
                .OrderByDescending(x => x.Value)
                .Take(10)
                .Select(x => $"`{x.Key}` — {x.Value}"));

        var roles = string.Join(
            ", ",
            noisyRoleNames.Select(x => $"`{x}`"));

        if (roles.Length > 1000)
            roles = roles[..997] + "...";

        var samples = string.Join(
            "\n",
            matches
                .Take(10)
                .Select(x =>
                    $"`{x.Id}` — {x.Timestamp:dd/MM/yyyy} — `{x.Author.Username}`"));

        var embed = new EmbedBuilder()
            .WithTitle("Noisy role-log cleanup — DRY RUN")
            .WithColor(0xFEE75C)
            .WithDescription("Nothing was deleted.")
            .AddField("Source", $"<#{SourceChannelId}>", true)
            .AddField("Noise destination", $"<#{NoiseChannelId}>", true)
            .AddField("Messages scanned", scanned, true)
            .AddField("Webhook messages", webhookMessages, true)
            .AddField("Would delete", matches.Count, true)
            .AddField("Noise roles", string.IsNullOrEmpty(roles) ? "None" : roles)
            .AddField(
                "Webhook usernames found",
                string.IsNullOrEmpty(authors) ? "None" : authors);

        if (!string.IsNullOrEmpty(samples))
            embed.AddField("Candidate samples", samples);

        await command.FollowupAsync(embed: embed.Build());
    }
}
