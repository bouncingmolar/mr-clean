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
        try
        {
            var downloaded = await ((IGuild)g).GetUserAsync(user, CacheMode.AllowDownload, new RequestOptions { CancelToken = token });
            // REST cannot prove voice state; a present but uncached member must wait for cache refresh.
            return downloaded == null ? new VoiceRoleMember(user, null, Array.Empty<ulong>(), false) : null;
        }
        catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
        { return new VoiceRoleMember(user, null, Array.Empty<ulong>(), false); }
    }
    public Task<bool> CanManageAsync(ulong guild, ulong user, ulong role, CancellationToken token)
    {
        var g = _client.GetGuild(guild); var r = g?.GetRole(role); var member = g?.GetUser(user);
        var bot = g?.CurrentUser;
        return Task.FromResult(g?.IsConnected == true && member != null && bot != null && r != null &&
            CanManageRole(g.Id, g.OwnerId, member, bot, bot.Hierarchy, r));
    }

    // Discord constrains the ROLE being assigned/removed, not the recipient's highest role.
    // Keep the existing owner exclusion as application policy, separate from API requirements.
    internal static bool CanManageRole(ulong guild, ulong owner, IGuildUser member, IGuildUser bot,
        int botHierarchy, IRole role) =>
        member.Id != owner && bot.GuildPermissions.ManageRoles && role.Id != guild &&
        !role.IsManaged && !role.Permissions.Administrator && !role.Permissions.ManageRoles &&
        !role.Permissions.ManageGuild && role.Position < botHierarchy;

    public Task AddAsync(ulong guild, ulong user, ulong role, CancellationToken token) =>
        _client.GetGuild(guild).GetUser(user).AddRoleAsync(role, new RequestOptions { CancelToken = token });
    public Task RemoveAsync(ulong guild, ulong user, ulong role, CancellationToken token) =>
        _client.GetGuild(guild).GetUser(user).RemoveRoleAsync(role, new RequestOptions { CancelToken = token });
}
