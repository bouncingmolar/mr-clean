using Discord;
using Discord.WebSocket;
using MrClean.Extensions;
using MrClean.Services.Retention;

namespace MrClean.Commands.Retention;

public class DeleteRetentionRuleCommand : ISlashCommandProvider
{
    private readonly IMessageRetentionRulesService _rules;

    public DeleteRetentionRuleCommand(IMessageRetentionRulesService rules)
    {
        _rules = rules;
    }

    public ApplicationCommandProperties Properties { get; } = new SlashCommandBuilder
        {
            Name = "delete-retention",
            Description = "Deletes a message retention rule",
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
        var rule = await _rules.DeleteAsync(id);

        await command.FollowupAsync(
            text: $"Retention rule #{id} deleted.",
            embed: rule.Embed);
    }
}
