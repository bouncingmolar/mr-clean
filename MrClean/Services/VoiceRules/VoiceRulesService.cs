using Microsoft.EntityFrameworkCore;
using MrClean.Data;
using MrClean.Models;

namespace MrClean.Services.VoiceRules;

public class VoiceRulesService
{
    public event Action<ulong, IReadOnlyList<VoiceChannelRule>>? Changed;
    private readonly IDbContextFactory<MrCleanDbContext> _factory;
    public VoiceRulesService(IDbContextFactory<MrCleanDbContext> factory) => _factory = factory;

    public static void RequireAdministrator(bool manageGuild, bool administrator)
    {
        if (!manageGuild && !administrator) throw new UnauthorizedAccessException("Manage Server permission is required.");
    }

    public static void Validate(VoiceChannelRule rule, ulong connectedRoleId)
    {
        if (rule.GuildId == 0 || rule.ChannelId == 0 || rule.DestinationId == 0)
            throw new ArgumentException("Guild, source and destination must be valid Discord IDs.");
        if (string.IsNullOrWhiteSpace(rule.ActivityName) || rule.ActivityName.Length > 100 ||
            string.IsNullOrWhiteSpace(rule.Wording) || rule.Wording.Length > 150 ||
            rule.ActivityName.Any(char.IsControl) || rule.Wording.Any(char.IsControl))
            throw new ArgumentException("Activity name/wording must be nonempty, single-line and at most 100/150 characters.");
        if (connectedRoleId == 0 && rule.RoleIds.Length > 0) throw new ArgumentException("Configure the protected Connected role ID before adding activity roles.");
        if (rule.RoleIds.Any(x => x == 0 || x == rule.GuildId || x == connectedRoleId))
            throw new ArgumentException("Activity roles cannot include @everyone, invalid IDs or the VC Roles Connected role.");
    }

    public async Task<List<VoiceChannelRule>> ListAsync(ulong guild, CancellationToken token = default)
    {
        await using var db = await _factory.CreateDbContextAsync(token);
        return await db.VoiceChannelRules.AsNoTracking().Where(x => x.GuildId == guild).OrderBy(x => x.ChannelId).ToListAsync(token);
    }

    public async Task SaveAsync(VoiceChannelRule rule, bool adding, ulong connectedRoleId, CancellationToken token = default)
    {
        Validate(rule, connectedRoleId);
        await using var db = await _factory.CreateDbContextAsync(token);
        var existing = await db.VoiceChannelRules.SingleOrDefaultAsync(x => x.GuildId == rule.GuildId && x.ChannelId == rule.ChannelId, token);
        if (adding && existing != null) throw new ArgumentException("A rule already exists; use /voice edit.");
        if (!adding && existing == null) throw new ArgumentException("No rule exists; use /voice add.");
        if (existing == null) { rule.Id = 0; db.Add(rule); }
        else { rule.Id = existing.Id; db.Entry(existing).CurrentValues.SetValues(rule); }
        await db.SaveChangesAsync(token);
        Changed?.Invoke(rule.GuildId, await ListAsync(rule.GuildId, token));
    }

    public async Task RemoveAsync(ulong guild, ulong channel, CancellationToken token = default)
    {
        await using var db = await _factory.CreateDbContextAsync(token);
        var rule = await db.VoiceChannelRules.SingleOrDefaultAsync(x => x.GuildId == guild && x.ChannelId == channel, token)
            ?? throw new ArgumentException("No rule exists for this channel.");
        db.Remove(rule);
        // Ownership deliberately survives rule removal until reconciliation confirms cleanup.
        await db.SaveChangesAsync(token);
        Changed?.Invoke(guild, await ListAsync(guild, token));
    }
}
