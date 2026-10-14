using Discord;
using Discord.WebSocket;
using MrClean.Extensions;
using MrClean.Services.Retention;

namespace MrClean.Commands.Retention;

public class EditRetentionRuleCommand : ISlashCommandProvider
{
    private readonly IMessageRetentionRulesService _rules;

    public EditRetentionRuleCommand(IMessageRetentionRulesService rules)
    {
        _rules = rules;
    }

    public ApplicationCommandProperties Properties { get; } = new SlashCommandBuilder
        {
            Name = "edit-retention",
            Description = "Edits a message retention rule",
            Options = new List<SlashCommandOptionBuilder>
            {
                new()
                {
                    Name = "id",
                    Description = "Retention rule ID.",
                    IsRequired = true,
                    Type = ApplicationCommandOptionType.Integer,
                    MinValue = 1
                },
                new()
                {
                    Name = "channel",
                    Description = "Channel in which the rule applies.",
                    IsRequired = true,
                    Type = ApplicationCommandOptionType.Channel
                },
                new()
                {
                    Name = "days",
                    Description = "Delete messages older than this many days.",
                    IsRequired = true,
                    Type = ApplicationCommandOptionType.Integer,
                    MinValue = 1
                },
                new()
                {
                    Name = "filter",
                    Description = "Message filter ID used to identify matching messages.",
                    IsRequired = true,
                    Type = ApplicationCommandOptionType.Integer,
                    MinValue = 1
                },
                new()
                {
                    Name = "preserve-replies",
                    Description = "Preserve messages that have human replies.",
                    IsRequired = true,
                    Type = ApplicationCommandOptionType.Boolean
                },
                new()
                {
                    Name = "bot",
                    Description = "Bot whose messages should be cleaned up.",
                    IsRequired = false,
                    Type = ApplicationCommandOptionType.User
                },
                new()
                {
                    Name = "author-id",
                    Description = "Author or webhook ID. Use instead of bot.",
                    IsRequired = false,
                    Type = ApplicationCommandOptionType.String
                },
                new()
                {
                    Name = "webhook-name",
                    Description = "Webhook username, e.g. FreeStuff. Use instead of bot or author-id.",
                    IsRequired = false,
                    Type = ApplicationCommandOptionType.String
                }
            }
        }
        .Build();

    public async Task HandleCommandInvocationAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var id = checked((int)command.GetOption<long>("id"));
        var author = command.GetOption<SocketUser>("bot");
        var authorIdText = command.GetOption<string>("author-id");
        var webhookName = command.GetOption<string>("webhook-name");
        var channel = command.GetOption<SocketGuildChannel>("channel")
                      ?? throw new ArgumentNullException("channel");
        var days = checked((int)command.GetOption<long>("days"));
        var preserveReplies = command.GetOption<bool>("preserve-replies");
        var filterId = checked((int)command.GetOption<long>("filter"));

        if (author != null && !author.IsBot)
        {
            await command.FollowupAsync("The selected user must be a bot.");
            return;
        }

        var targetCount =
            (author != null ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(authorIdText) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(webhookName) ? 1 : 0);

        if (targetCount != 1)
        {
            await command.FollowupAsync(
                "Specify exactly one of bot, author-id, or webhook-name.");
            return;
        }

        ulong authorId;

        if (author != null)
        {
            authorId = author.Id;
        }
        else if (!string.IsNullOrWhiteSpace(authorIdText) &&
                 ulong.TryParse(authorIdText, out var parsedAuthorId))
        {
            authorId = parsedAuthorId;
        }
        else if (!string.IsNullOrWhiteSpace(webhookName))
        {
            authorId = 0;
            webhookName = webhookName.Trim();
        }
        else
        {
            await command.FollowupAsync(
                "author-id must be a valid numeric Discord ID.");
            return;
        }

        if (channel is not SocketTextChannel)
        {
            await command.FollowupAsync("The selected channel must be a text channel.");
            return;
        }

        var rule = await _rules.EditAsync(
            id,
            channel.Id,
            authorId,
            webhookName,
            days,
            preserveReplies,
            filterId);

        await command.FollowupAsync(
            text: $"Retention rule #{id} updated.",
            embed: rule.Embed);
    }
}
