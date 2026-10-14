using Discord;
using Discord.WebSocket;
using MrClean.Services.Retention;

namespace MrClean.Commands.Retention;

public class ListRetentionRulesCommand : ISlashCommandProvider
{
    private readonly IMessageRetentionRulesService _rules;

    public ListRetentionRulesCommand(IMessageRetentionRulesService rules)
    {
        _rules = rules;
    }

    public ApplicationCommandProperties Properties { get; } = new SlashCommandBuilder
    {
        Name = "list-retention",
        Description = "Lists all message retention rules"
    }.Build();

    public async Task HandleCommandInvocationAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var rules = await _rules.ListAsync();

        if (rules.Count == 0)
        {
            await command.FollowupAsync("There are no retention rules.");
            return;
        }

        await command.FollowupAsync(
            text: $"There are {rules.Count} retention rule(s).",
            embeds: rules.Select(r => r.Embed).ToArray());
    }
}
