using Discord;
using Discord.WebSocket;
using MrClean.Services.RoleLogging;

namespace MrClean.Commands.RoleLogging;

public class RoleLoggingSettingsCommand : ISlashCommandProvider
{
    private readonly IRoleLoggingService _service;

    public RoleLoggingSettingsCommand(IRoleLoggingService service)
    {
        _service = service;
    }

    public ApplicationCommandProperties Properties { get; } =
        new SlashCommandBuilder()
            .WithName("role-logging-settings")
            .WithDescription("Shows the current role logging configuration.")
            .Build();

    public async Task HandleCommandInvocationAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var config = await _service.GetAsync();

        if (config == null)
        {
            await command.FollowupAsync(
                "Role logging is not configured.");
            return;
        }

        var routes = config.Routes.Count == 0
            ? "None"
            : string.Join(
                "\n",
                config.Routes.Select(
                    x => $"<@&{x.RoleId}> → <#{x.ChannelId}>"));

        var embed = new EmbedBuilder()
            .WithTitle("Role logging")
            .AddField(
                "Status",
                config.Enabled ? "Enabled" : "Disabled",
                true)
            .AddField(
                "Default log",
                $"<#{config.DefaultChannelId}>",
                true)
            .AddField("Special routes", routes)
            .Build();

        await command.FollowupAsync(embed: embed);
    }
}
