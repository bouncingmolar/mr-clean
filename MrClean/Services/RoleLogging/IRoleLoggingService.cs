using MrClean.Models;

namespace MrClean.Services.RoleLogging;

public interface IRoleLoggingService
{
    Task<RoleLoggingConfiguration?> GetAsync();

    Task<RoleLoggingConfiguration> ConfigureAsync(
        ulong defaultChannelId);

    Task<RoleLoggingConfiguration> SetRouteAsync(
        ulong roleId,
        ulong channelId);

    Task<RoleLoggingConfiguration> RemoveRouteAsync(ulong roleId);

    Task<RoleLoggingConfiguration> EnableAsync();
    Task<RoleLoggingConfiguration> DisableAsync();
}
