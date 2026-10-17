using Discord;
using Microsoft.EntityFrameworkCore;
using MrClean.Data;
using MrClean.Models;

namespace MrClean.Services.Activities;

public class ActivityRulesService
{
    private readonly IDbContextFactory<MrCleanDbContext> _factory;
    public ActivityRulesService(IDbContextFactory<MrCleanDbContext> factory) => _factory = factory;
    public event Action<ulong>? Changed;
    public async Task<(List<ActivityRule> Rules, List<ActivityFallback> Fallbacks, bool Enabled)> GetAsync(ulong guild, CancellationToken token = default)
    {
        await using var db = await _factory.CreateDbContextAsync(token);
        return (await db.ActivityRules.AsNoTracking().Where(x => x.GuildId == guild).OrderBy(x => x.Id).ToListAsync(token),
            await db.ActivityFallbacks.AsNoTracking().Where(x => x.GuildId == guild).ToListAsync(token),
            await db.ActivityGuildSettings.AnyAsync(x => x.GuildId == guild && x.Enabled, token));
    }
    public static void Validate(ulong guild, string name, ActivityType type, ulong role, ulong connected, ulong? application)
    {
        if (guild == 0 || role == 0 || role == guild || connected == 0 || role == connected)
            throw new ArgumentException("Choose valid guild/activity/role IDs; Connected is protected. Supported types: Playing, Listening, Watching, Streaming.");
        ValidateMatch(name, type, application);
    }
    public static void ValidateMatch(string name, ActivityType type, ulong? application)
    {
        if (application == 0 || !ActivityMatching.Supported(type)) throw new ArgumentException("Choose a supported type and valid application ID.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.Any(char.IsControl))
            throw new ArgumentException("Activity names must be exact, nonempty single-line values up to 100 characters.");
    }
    public async Task SaveAsync(ActivityRule rule, bool adding, ulong connected)
    {
        Validate(rule.GuildId, rule.Name, rule.Type, rule.RoleId, connected, rule.ApplicationId);
        await using var db = await _factory.CreateDbContextAsync();
        var existing = adding ? await db.ActivityRules.SingleOrDefaultAsync(x => x.GuildId == rule.GuildId && x.Type == rule.Type && x.Name == rule.Name) :
            await db.ActivityRules.SingleOrDefaultAsync(x => x.GuildId == rule.GuildId && x.Id == rule.Id);
        if (adding && existing != null) throw new ArgumentException("This exact name/type is already registered; use /activity edit.");
        if (!adding && existing == null) throw new ArgumentException("Rule ID not found in this guild.");
        if (existing == null) { rule.Id = 0; db.Add(rule); }
        else { rule.Id = existing.Id; db.Entry(existing).CurrentValues.SetValues(rule); }
        await db.SaveChangesAsync(); Changed?.Invoke(rule.GuildId);
    }
    public async Task RemoveAsync(ulong guild, int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var rule = await db.ActivityRules.SingleOrDefaultAsync(x => x.GuildId == guild && x.Id == id) ?? throw new ArgumentException("Rule ID not found.");
        db.Remove(rule); await db.SaveChangesAsync(); Changed?.Invoke(guild); // Never delete a Discord role.
    }
    public async Task FallbackAsync(ActivityFallback value, ulong connected)
    {
        Validate(value.GuildId, "fallback", value.Type, value.RoleId, connected, null);
        await using var db = await _factory.CreateDbContextAsync();
        var old = await db.ActivityFallbacks.FindAsync(value.GuildId, value.Type);
        if (old == null) db.Add(value); else db.Entry(old).CurrentValues.SetValues(value);
        await db.SaveChangesAsync(); Changed?.Invoke(value.GuildId);
    }
    public async Task EnableAsync(ulong guild, bool enabled)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var value = await db.ActivityGuildSettings.FindAsync(guild);
        if (value == null) { value = new ActivityGuildSettings { GuildId = guild }; db.Add(value); }
        value.Enabled = enabled; await db.SaveChangesAsync(); Changed?.Invoke(guild);
    }
}
