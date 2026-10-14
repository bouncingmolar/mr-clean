using Discord;
using Discord.WebSocket;
using MrClean.Extensions;
using MrClean.Services.RoleLogging;

namespace MrClean.Commands.RoleLogging;

public class SetRoleLogRouteCommand : ISlashCommandProvider
{
    private readonly IRoleLoggingService _service;

    public SetRoleLogRouteCommand(IRoleLoggingService service)
    {
        _service = service;
    }

    public ApplicationCommandProperties Properties { get; } =
        new SlashCommandBuilder()
            .WithName("role-log-route")
            .WithDescription("Routes changes to a role into a specific channel.")
            .AddOption(
                "role",
                ApplicationCommandOptionType.Role,
                "Role to route.",
                isRequired: true)
            .AddOption(
                "channel",
                ApplicationCommandOptionType.Channel,
                "Channel for changes to this role.",
                isRequired: true)
            .Build();

    public async Task HandleCommandInvocationAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var role = command.GetOption<SocketRole>("role")
                   ?? throw new ArgumentException("Role is required.");

        var channel =
            command.GetOption<SocketGuildChannel>("channel")
            ?? throw new ArgumentException("Channel is required.");

        await _service.SetRouteAsync(role.Id, channel.Id);

        await command.FollowupAsync(
            $"Changes to `{role.Name}` → <#{channel.Id}>.");
    }
}
