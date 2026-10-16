using Discord;
using Discord.WebSocket;
using MrClean.Extensions;
using MrClean.Services.Retention;

namespace MrClean.Commands.Retention;

public class DisableRetentionRuleCommand : ISlashCommandProvider
{
    private readonly IMessageRetentionRulesService _rules;

    public DisableRetentionRuleCommand(IMessageRetentionRulesService rules)
    {
        _rules = rules;
    }

    public ApplicationCommandProperties Properties { get; } = new SlashCommandBuilder
        {
            Name = "disable-retention",
            Description = "Disables a message retention rule",
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
        var rule = await _rules.DisableAsync(id);

        await command.FollowupAsync(
            text: $"Retention rule #{id} disabled.",
            embed: rule.Embed);
    }
}
