using System.Reflection;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MrClean.Commands;
using MrClean.Commands.TypeReaders;
using MrClean.Configuration;
using MrClean.Data;
using MrClean.Models;
using MrClean.Services.RoleLogging;

namespace MrClean.Services;

public class DiscordBotService : BackgroundService
{
    private readonly ILogger<DiscordBotService> _logger;

    private readonly DiscordOptions _options;

    private readonly DiscordSocketClient _client;

    private readonly SlashCommandDispatcher _dispatcher;

    private readonly MessageFilteringService _filter;

    private readonly CommandService _commandService;

    private readonly IServiceProvider _services;

    private readonly IDbContextFactory<MrCleanDbContext> _contextFactory;

    private readonly RoleChangeLogger _roleChangeLogger;

    public DiscordBotService(
        ILogger<DiscordBotService> logger,
        IOptions<DiscordOptions> options,
        IDbContextFactory<MrCleanDbContext> contextFactory,
        SlashCommandDispatcher dispatcher,
        MessageFilteringService filter,
        CommandService commandService,
        IServiceProvider services,
        DiscordSocketClient client,
        RoleChangeLogger roleChangeLogger)
    {
        _logger = logger;
        _contextFactory = contextFactory;
        _options = options.Value;
        _dispatcher = dispatcher;
        _filter = filter;
        _commandService = commandService;
        _services = services;
        _client = client;
        _roleChangeLogger = roleChangeLogger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Preparing the database");

        await UpdateDatabaseAsync();

        _logger.LogInformation("Starting the Mr.Clean Discord bot at {time}", DateTimeOffset.Now);

        _client.Ready += HandleReadyEventAsync;
        _client.Connected += HandleConnectedEventAsync;
        _client.Log += HandleLogMessageAsync;
        _client.MessageReceived += HandleMessageAsync;

        _filter.RegisterMessageHandler(_client);
        _roleChangeLogger.Register(_client);

        _commandService.AddTypeReader<SpecificationEntityType>(new SpecificationEntityTypeReader());
        _commandService.AddTypeReader<ISnowflakeEntity>(new SnowflakeEntityTypeReader());

        await _commandService.AddModuleAsync<MessageCommandsModule>(_services);


        await _client.LoginAsync(TokenType.Bot, _options.Token);
        await _client.StartAsync();
        await Task.Delay(-1, stoppingToken);
    }

    private async Task UpdateDatabaseAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
    }

    private async Task HandleReadyEventAsync()
    {
        try
        {
            var guild = _client.GetGuild(_options.GuildId);

            // Validate, that the guild is configuration is correct
            if (guild == null)
            {
                throw new ApplicationException($"The configured guild (id = {_options.GuildId}) cannot be found!");
            }

            await _client.SetGameAsync(type: ActivityType.Watching, name: $"for messages in {guild.Name}");
            await _dispatcher.RegisterSlashCommandsAsync(_client);
        }
        catch (Exception exception)
        {
            _logger.LogCritical("Exception in ready event handler: {exception}", exception);
            throw;
        }
    }

    private Task HandleConnectedEventAsync()
    {
        _logger.LogInformation("Connected to the Discord gateway");
        return Task.CompletedTask;
    }

    private Task HandleLogMessageAsync(LogMessage message)
    {
        if (message.Exception != null)
            _logger.LogError(message.Exception, "{message}", message.Message);
        else
            _logger.LogInformation("{message}", message.Message);

        return Task.CompletedTask;
    }

    private async Task HandleMessageAsync(SocketMessage raw)
    {
        if (raw is not SocketUserMessage {Channel: SocketTextChannel channel} message) return;
        if (channel.Guild is null) return;


        var position = 0;
        if (!message.Author.IsBot && message.HasMentionPrefix(_client.CurrentUser, ref position))
        {
            try
            {
                var id = message.Author.Id;
                var member = channel.Guild.GetUser(id) ??
                             throw new ApplicationException($"Cannot obtain the guild member with ID = {id}");

                if (!member.GuildPermissions.ManageGuild)
                {
                    throw new ApplicationException("Sorry, this bot can be only used by the mods.");
                }

                var result = await _commandService.ExecuteAsync(
                    new SocketCommandContext(_client, message),
                    position,
                    _services
                );

                if (!result.IsSuccess)
                {
                    await message.ReplyAsync(
                        "**Sorry, there was an issue with handling this command.**\n" +
                        $"```{result.ErrorReason}```"
                    );
                }
            }
            catch (Exception exception)
            {
                await message.ReplyAsync(
                    "**Sorry, there was an issue with handling this command.**\n" +
                    $"```{exception.Message}```"
                );
            }
        }
    }
}