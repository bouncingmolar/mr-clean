using Discord;
using Discord.WebSocket;
using MrClean.Extensions;
using MrClean.Services.RoleLogging;

namespace MrClean.Commands.RoleLogging;

public class ConfigureRoleLoggingCommand : ISlashCommandProvider
{
    private readonly IRoleLoggingService _service;

    public ConfigureRoleLoggingCommand(IRoleLoggingService service)
    {
        _service = service;
    }

    public ApplicationCommandProperties Properties { get; } =
        new SlashCommandBuilder()
            .WithName("configure-role-logging")
            .WithDescription("Sets the default channel for role changes.")
            .AddOption(
                "default-channel",
                ApplicationCommandOptionType.Channel,
                "Default channel for role changes.",
                isRequired: true)
            .Build();

    public async Task HandleCommandInvocationAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var channel =
            command.GetOption<SocketGuildChannel>("default-channel")
            ?? throw new ArgumentException("Default channel is required.");

        await _service.ConfigureAsync(channel.Id);

        await command.FollowupAsync(
            $"Default role log → <#{channel.Id}>.");
    }
}
