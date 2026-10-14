using Discord;
using Discord.WebSocket;
using MrClean.Extensions;
using MrClean.Services.Retention;

namespace MrClean.Commands.Retention;

public class EnableRetentionRuleCommand : ISlashCommandProvider
{
    private readonly IMessageRetentionRulesService _rules;

    public EnableRetentionRuleCommand(IMessageRetentionRulesService rules)
    {
        _rules = rules;
    }

    public ApplicationCommandProperties Properties { get; } = new SlashCommandBuilder
        {
            Name = "enable-retention",
            Description = "Enables a message retention rule",
            Options = new List<SlashCommandOptionBuilder>
            {
                new()
                {
                    Name = "id",
                    Description = "Retention rule ID.",
                    IsRequired = true,
                    Type = ApplicationCommandOptionType.Integer,
                    MinValue = 1
                }
            }
        }
        .Build();

    public async Task HandleCommandInvocationAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var id = checked((int)command.GetOption<long>("id"));
        var rule = await _rules.EnableAsync(id);

        await command.FollowupAsync(
            text: $"Retention rule #{id} enabled.",
            embed: rule.Embed);
    }
}
