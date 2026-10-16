using MrClean.Services;
using MrClean.Services.AvatarLogging;

namespace MrClean.Extensions;

public static class DiscordHostedServiceExtensions
{
    public static IServiceCollection AddDiscordHostedServices(this IServiceCollection services)
    {
        // The avatar event subscriptions must exist before Discord can emit Ready.
        services.AddHostedService<AvatarChangeLogger>();
        services.AddHostedService<DiscordBotService>();
        return services;
    }
}
