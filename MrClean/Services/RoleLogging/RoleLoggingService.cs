using Microsoft.EntityFrameworkCore;
using MrClean.Data;
using MrClean.Models;

namespace MrClean.Services.RoleLogging;

public class RoleLoggingService : IRoleLoggingService
{
    private readonly IDbContextFactory<MrCleanDbContext> _factory;

    public RoleLoggingService(IDbContextFactory<MrCleanDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<RoleLoggingConfiguration?> GetAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();

        return await db.RoleLoggingConfigurations
            .Include(x => x.Routes)
            .FirstOrDefaultAsync();
    }

    public async Task<RoleLoggingConfiguration> ConfigureAsync(
        ulong defaultChannelId)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var config = await db.RoleLoggingConfigurations
            .Include(x => x.Routes)
            .FirstOrDefaultAsync();

        if (config == null)
        {
            config = new RoleLoggingConfiguration();
            db.RoleLoggingConfigurations.Add(config);
        }

        config.DefaultChannelId = defaultChannelId;

        await db.SaveChangesAsync();
        return config;
    }

    public async Task<RoleLoggingConfiguration> SetRouteAsync(
        ulong roleId,
        ulong channelId)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var config = await db.RoleLoggingConfigurations
            .Include(x => x.Routes)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException(
                "Configure role logging first.");

        var route = config.Routes
            .FirstOrDefault(x => x.RoleId == roleId);

        if (route == null)
        {
            route = new RoleLogRoute
            {
                RoleId = roleId,
                ChannelId = channelId
            };

            config.Routes.Add(route);
        }
        else
        {
            route.ChannelId = channelId;
        }

        await db.SaveChangesAsync();
        return config;
    }

    public async Task<RoleLoggingConfiguration> RemoveRouteAsync(ulong roleId)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var config = await db.RoleLoggingConfigurations
            .Include(x => x.Routes)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException(
                "Role logging is not configured.");

        var route = config.Routes
            .FirstOrDefault(x => x.RoleId == roleId);

        if (route != null)
            db.RoleLogRoutes.Remove(route);

        await db.SaveChangesAsync();
        return config;
    }

    public Task<RoleLoggingConfiguration> EnableAsync()
        => SetEnabledAsync(true);

    public Task<RoleLoggingConfiguration> DisableAsync()
        => SetEnabledAsync(false);

    private async Task<RoleLoggingConfiguration> SetEnabledAsync(bool enabled)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var config = await db.RoleLoggingConfigurations
            .Include(x => x.Routes)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException(
                "Configure role logging first.");

        config.Enabled = enabled;
        await db.SaveChangesAsync();

        return config;
    }
}
