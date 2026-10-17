using Discord;
using Discord.WebSocket;

namespace MrClean.Services.VoiceRules;

public class DiscordVoiceRoleGateway : IVoiceRoleGateway
{
    private readonly DiscordSocketClient _client;
    public DiscordVoiceRoleGateway(DiscordSocketClient client) => _client = client;
    public async Task<VoiceRoleMember?> MemberAsync(ulong guild, ulong user, CancellationToken token)
    {
        var g = _client.GetGuild(guild);
        var member = g?.GetUser(user);
        if (g?.IsConnected != true) return null;
        if (member != null) return new VoiceRoleMember(user, member.VoiceChannel?.Id, member.Roles.Select(x => x.Id).ToArray());
        var downloaded = await ((IGuild)g).GetUserAsync(user, CacheMode.AllowDownload, new RequestOptions { CancelToken = token });
        // REST cannot prove voice state; a present but uncached member must wait for cache refresh.
        return downloaded == null ? new VoiceRoleMember(user, null, Array.Empty<ulong>()) : null;
    }
    public Task<bool> CanManageAsync(ulong guild, ulong user, ulong role, CancellationToken token)
    {
        var g = _client.GetGuild(guild); var r = g?.GetRole(role); var member = g?.GetUser(user);
        var bot = g?.CurrentUser;
        return Task.FromResult(g?.IsConnected == true && member != null && bot != null && r != null &&
            !r.IsManaged && r.Id != guild && !r.Permissions.Administrator && !r.Permissions.ManageRoles &&
            !r.Permissions.ManageGuild && bot.GuildPermissions.ManageRoles && r.Position < bot.Hierarchy &&
            member.Id != g.OwnerId && member.Hierarchy < bot.Hierarchy);
    }
    public Task AddAsync(ulong guild, ulong user, ulong role, CancellationToken token) =>
        _client.GetGuild(guild).GetUser(user).AddRoleAsync(role, new RequestOptions { CancelToken = token });
    public Task RemoveAsync(ulong guild, ulong user, ulong role, CancellationToken token) =>
        _client.GetGuild(guild).GetUser(user).RemoveRoleAsync(role, new RequestOptions { CancelToken = token });
}
