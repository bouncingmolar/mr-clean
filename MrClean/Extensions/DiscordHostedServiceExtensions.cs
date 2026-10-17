using MrClean.Services;
using MrClean.Services.AvatarLogging;

namespace MrClean.Extensions;

public static class DiscordHostedServiceExtensions
{
    public static IServiceCollection AddDiscordHostedServices(this IServiceCollection services)
    {
        // Logger subscriptions must exist before Discord can emit gateway events.
        services.AddHostedService<AvatarChangeLogger>();
        services.AddHostedService<MrClean.Services.VoiceLogging.VoiceActivityLogger>();
        services.AddHostedService<DiscordBotService>();
        return services;
    }
}
