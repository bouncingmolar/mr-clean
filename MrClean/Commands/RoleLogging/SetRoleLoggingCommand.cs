using Discord;
using Discord.WebSocket;
using MrClean.Extensions;
using MrClean.Services.RoleLogging;

namespace MrClean.Commands.RoleLogging;

public class SetRoleLoggingCommand : ISlashCommandProvider
{
    private readonly IRoleLoggingService _service;

    public SetRoleLoggingCommand(IRoleLoggingService service)
    {
        _service = service;
    }

    public ApplicationCommandProperties Properties { get; } =
        new SlashCommandBuilder()
            .WithName("set-role-logging")
            .WithDescription("Enables or disables Mr. Clean role logging.")
            .AddOption(
                "enabled",
                ApplicationCommandOptionType.Boolean,
                "Whether role logging should be enabled.",
                isRequired: true)
            .Build();

    public async Task HandleCommandInvocationAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var enabled = command.GetOption<bool>("enabled");

        var config = enabled
            ? await _service.EnableAsync()
            : await _service.DisableAsync();

        await command.FollowupAsync(
            $"Role logging is now **{(config.Enabled ? "enabled" : "disabled")}**.");
    }
}
