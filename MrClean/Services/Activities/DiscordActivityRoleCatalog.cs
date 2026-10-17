using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;

namespace MrClean.Services.Activities;

public class DiscordActivityRoleCatalog : IActivityRoleCatalog
{
    private readonly DiscordSocketClient _client;
    private readonly ConcurrentDictionary<(ulong Guild, ulong Role), byte> _pending = new();
    public DiscordActivityRoleCatalog(DiscordSocketClient client) => _client = client;
    private static ActivityRoleCandidate Describe(SocketGuild g, IRole r) => new(r.Id, r.Name,
        g.CurrentUser.GuildPermissions.ManageRoles && r.Id != g.Id && !r.IsManaged && r.Position < g.CurrentUser.Hierarchy &&
        !r.Permissions.Administrator && !r.Permissions.ManageRoles && !r.Permissions.ManageGuild);
    public async Task<IReadOnlyList<ActivityRoleCandidate>> ListAsync(ulong guild)
    {
        var g = _client.GetGuild(guild) ?? throw new ArgumentException("Server unavailable.");
        var roles = g.Roles.Select(r => Describe(g, r)).ToList();
        foreach (var key in _pending.Keys.Where(x => x.Guild == guild))
        {
            if (roles.Any(x => x.Id == key.Role)) { _pending.TryRemove(key, out _); continue; }
            // CreateRoleAsync returns before its gateway event may reach the socket cache.
            try
            {
                var role = await ((IGuild)g).GetRoleAsync(key.Role);
                if (role == null) _pending.TryRemove(key, out _); else roles.Add(Describe(g, role));
            }
            catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
            { _pending.TryRemove(key, out _); }
        }
        return roles;
    }
    public async Task<ActivityRoleCandidate> CreateAsync(ulong guild, string name)
    {
        var g = _client.GetGuild(guild) ?? throw new ArgumentException("Server unavailable.");
        if (!g.CurrentUser.GuildPermissions.ManageRoles) throw new ArgumentException("Manage Roles is required.");
        var role = await g.CreateRoleAsync(name, new GuildPermissions(0), isHoisted: false, isMentionable: false);
        _pending[(guild, role.Id)] = 0;
        return Describe(g, role);
    }
}
