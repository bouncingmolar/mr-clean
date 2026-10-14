using Discord;
using Discord.WebSocket;
using MrClean.Extensions;
using MrClean.Services.Retention;

namespace MrClean.Commands.Retention;

public class TestRetentionRuleCommand : ISlashCommandProvider
{
    private readonly IMessageRetentionRulesService _rules;
    private readonly MessageRetentionScanner _scanner;
    private readonly DiscordSocketClient _client;

    public TestRetentionRuleCommand(
        IMessageRetentionRulesService rules,
        MessageRetentionScanner scanner,
        DiscordSocketClient client)
    {
        _rules = rules;
        _scanner = scanner;
        _client = client;
    }

    public ApplicationCommandProperties Properties { get; } =
        new SlashCommandBuilder
        {
            Name = "test-retention",
            Description = "Test a retention rule without deleting anything",
            Options = new List<SlashCommandOptionBuilder>
            {
                new()
                {
                    Name = "id",
                    Description = "Retention rule ID",
                    Type = ApplicationCommandOptionType.Integer,
                    IsRequired = true,
                    MinValue = 1
                }
            }
        }.Build();

    public async Task HandleCommandInvocationAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var id = (int)command.GetOption<long>("id");
        var rule = await _rules.GetAsync(id);

        var channel = _client.GetChannel(rule.ChannelId) as IMessageChannel;

        if (channel == null)
        {
            await command.FollowupAsync(
                $"Could not access <#{rule.ChannelId}>.");
            return;
        }

        var result = await _scanner.ScanAsync(channel, rule);

        var embed = new EmbedBuilder()
            .WithTitle($"Retention test #{rule.Id}")
            .WithColor(0xFEE75C)
            .WithDescription("Dry run only — no messages were deleted.")
            .AddField("Channel", $"<#{rule.ChannelId}>", true)
            .AddField(
                "Target",
                !string.IsNullOrWhiteSpace(rule.WebhookUsername)
                    ? $"Webhook named `{rule.WebhookUsername}`"
                    : $"<@{rule.AuthorId}> (`{rule.AuthorId}`)",
                true)
            .AddField("Age", $"{rule.MaxAgeDays} days", true)
            .AddField("Messages scanned", result.MessagesScanned, true)
            .AddField("Target bot messages", result.TargetAuthorMessages, true)
            .AddField("At least minimum age", result.OldEnoughMessages, true)
            .AddField("Filter matches", result.FilterMatches, true)
            .AddField("Filter misses", result.FilterMisses, true)
            .AddField("No expiry found", result.MissingExpiry, true)
            .AddField("Not yet expired", result.NotYetExpired, true)
            .AddField("Would delete", result.DeleteCandidates.Count, true)
            .AddField(
                "Preserved by human reply",
                result.PreservedForHumanReply.Count,
                true)
            .AddField(
                "Preserved by reaction",
                result.PreservedForReaction.Count,
                true);

        var authors = string.Join(
            "\n",
            result.Authors
                .OrderByDescending(x => x.Value.Count)
                .Take(15)
                .Select(x =>
                    $"{x.Value.Name} — {x.Value.Count} messages — " +
                    $"ID `{x.Key}` — {(x.Value.IsBot ? "bot" : "human")}"));

        embed.AddField(
            "Top authors",
            string.IsNullOrEmpty(authors) ? "None" : authors);

        if (result.FilterMissSamples.Count > 0)
        {
            var samples = string.Join(
                "\n\n",
                result.FilterMissSamples);

            // Discord embed fields are limited to 1024 characters.
            if (samples.Length > 1024)
                samples = samples[..1021] + "...";

            embed.AddField(
                "Sample old target messages that missed the filter",
                samples);
        }

        await command.FollowupAsync(
            embed: embed.Build());
        return;

    }
}
