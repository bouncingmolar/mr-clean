using Discord;
using Discord.WebSocket;
using MrClean.Extensions;
using MrClean.Services.Retention;

namespace MrClean.Commands.Retention;

public class RunRetentionRuleCommand : ISlashCommandProvider
{
    private readonly IMessageRetentionRulesService _rules;
    private readonly MessageRetentionScanner _scanner;
    private readonly DiscordSocketClient _client;

    public RunRetentionRuleCommand(
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
            Name = "run-retention",
            Description = "Runs a retention rule and deletes eligible messages",
            Options = new List<SlashCommandOptionBuilder>
            {
                new()
                {
                    Name = "id",
                    Description = "Retention rule ID",
                    Type = ApplicationCommandOptionType.Integer,
                    IsRequired = true,
                    MinValue = 1
                },
                new()
                {
                    Name = "confirm",
                    Description = "Must be true to actually delete messages",
                    Type = ApplicationCommandOptionType.Boolean,
                    IsRequired = true
                }
            }
        }.Build();

    public async Task HandleCommandInvocationAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var id = checked((int)command.GetOption<long>("id"));
        var confirm = command.GetOption<bool>("confirm");

        if (!confirm)
        {
            await command.FollowupAsync(
                "Nothing deleted. Use /test-retention first, then run again with confirm:true.");
            return;
        }

        var rule = await _rules.GetAsync(id);

        var channel = _client.GetChannel(rule.ChannelId) as IMessageChannel;

        if (channel == null)
        {
            await command.FollowupAsync(
                $"Could not access <#{rule.ChannelId}>.");
            return;
        }

        // Re-scan immediately before deletion. Never rely on an earlier dry run.
        var result = await _scanner.ScanAsync(channel, rule);

        var deleted = 0;
        var failed = 0;

        foreach (var message in result.DeleteCandidates)
        {
            try
            {
                await message.DeleteAsync();
                deleted++;
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine(
                    $"Failed to delete retention message {message.Id}: {ex}");
            }
        }

        var embed = new EmbedBuilder()
            .WithTitle($"Retention run #{rule.Id}")
            .WithColor(failed == 0 ? new Color(0x57F287) : new Color(0xFEE75C))
            .AddField("Messages scanned", result.MessagesScanned, true)
            .AddField("Eligible", result.DeleteCandidates.Count, true)
            .AddField("Deleted", deleted, true)
            .AddField("Failed", failed, true)
            .AddField(
                "Preserved by human reply",
                result.PreservedForHumanReply.Count,
                true)
            .AddField(
                "Preserved by reaction",
                result.PreservedForReaction.Count,
                true);

        await command.FollowupAsync(
            embed: embed.Build());
    }
}
