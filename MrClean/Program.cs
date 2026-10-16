using Discord;
using MrClean.Commands.Retention;
using MrClean.Commands.RoleLogging;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using MrClean.Commands;
using MrClean.Commands.Filters;
using MrClean.Configuration;
using MrClean.Data;
using MrClean.Extensions;
using MrClean.Services;
using MrClean.Services.Filters;
using MrClean.Services.Retention;
using MrClean.Services.RoleLogging;
using MrClean.Services.AvatarLogging;
using Discord.WebSocket;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, builder) => { builder.AddEnvironmentVariables(); })
    .ConfigureServices((context, services) =>
    {
        services.Configure<DiscordOptions>(context.Configuration.GetRequiredSection(DiscordOptions.Section));

        services.AddCommands(collection => collection
            .AddCommand<AllowMessageFilterSpecificationCommand>()
            .AddCommand<CreateMessageFilterCommand>()
            .AddCommand<EditMessageFilterCommand>()
            .AddCommand<DeleteMessageFilterCommand>()
            .AddCommand<DenyMessageFilterSpecificationCommand>()
            .AddCommand<DisableFilterCommand>()
            .AddCommand<EnableFilterCommand>()
            .AddCommand<ListMessageFiltersCommand>()
            .AddCommand<ResetMessageFilterSpecificationCommand>()
            .AddCommand<CreateRetentionRuleCommand>()
            .AddCommand<EditRetentionRuleCommand>()
            .AddCommand<DeleteRetentionRuleCommand>()
            .AddCommand<DisableRetentionRuleCommand>()
            .AddCommand<EnableRetentionRuleCommand>()
            .AddCommand<ListRetentionRulesCommand>()
            .AddCommand<TestRetentionRuleCommand>()
            .AddCommand<RunRetentionRuleCommand>()
            .AddCommand<ConfigureRoleLoggingCommand>()
            .AddCommand<SetRoleLogRouteCommand>()
            .AddCommand<RemoveRoleLogRouteCommand>()
            .AddCommand<RoleLoggingSettingsCommand>()
            .AddCommand<SetRoleLoggingCommand>()
            .AddCommand<TestNoisyRoleLogCleanupCommand>()
        );

        services.AddTransient<MessageFilteringService>();
        services.AddSingleton<IMessageFiltersService, MessageFiltersService>();
        services.AddSingleton<IMessageRetentionRulesService, MessageRetentionRulesService>();
        services.AddSingleton<CommandService>();
        services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
        {
            AlwaysDownloadUsers = true,
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers | GatewayIntents.MessageContent,
        }));
        services.AddSingleton<MessageRetentionScanner>();
        services.AddSingleton<IRoleLoggingService, RoleLoggingService>();
        services.AddSingleton<RoleChangeLogger>();
        services.AddDiscordHostedServices();
        services.AddHostedService<TimedMuteService>();
        services.AddHostedService<MessageRetentionBackgroundService>();
        services.AddDbContextFactory<MrCleanDbContext>(options =>
        {
            options.UseNpgsql(context.Configuration.GetConnectionString("Default"));
        });
    })
    .Build();

await host.RunAsync();