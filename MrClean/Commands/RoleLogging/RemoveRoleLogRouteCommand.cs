using Discord;
using Discord.WebSocket;
using MrClean.Extensions;
using MrClean.Services.RoleLogging;

namespace MrClean.Commands.RoleLogging;

public class RemoveRoleLogRouteCommand : ISlashCommandProvider
{
    private readonly IRoleLoggingService _service;

    public RemoveRoleLogRouteCommand(IRoleLoggingService service)
    {
        _service = service;
    }

    public ApplicationCommandProperties Properties { get; } =
        new SlashCommandBuilder()
            .WithName("remove-role-log-route")
            .WithDescription("Returns a role to the default role log.")
            .AddOption(
                "role",
                ApplicationCommandOptionType.Role,
                "Role whose special route should be removed.",
                isRequired: true)
            .Build();

    public async Task HandleCommandInvocationAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var role = command.GetOption<SocketRole>("role")
                   ?? throw new ArgumentException("Role is required.");

        await _service.RemoveRouteAsync(role.Id);

        await command.FollowupAsync(
            $"`{role.Name}` now uses the default role log.");
    }
}
