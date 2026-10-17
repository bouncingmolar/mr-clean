using System.Collections.Concurrent;
using System.Text.Json;
using MrClean.Services.Activities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MrClean.Configuration;
using MrClean.Data;
using MrClean.Models;

namespace MrClean.Services.VoiceRules;

public record VoiceRoleMember(ulong UserId, ulong? ChannelId, IReadOnlyCollection<ulong> Roles, bool Present = true);
public interface IVoiceRoleGateway
{
    Task<VoiceRoleMember?> MemberAsync(ulong guild, ulong user, CancellationToken token);
    Task<bool> CanManageAsync(ulong guild, ulong user, ulong role, CancellationToken token);
    Task AddAsync(ulong guild, ulong user, ulong role, CancellationToken token);
    Task RemoveAsync(ulong guild, ulong user, ulong role, CancellationToken token);
}

public class VoiceRoleReconciler
{
    private readonly IDbContextFactory<MrCleanDbContext> _factory;
    private readonly VoiceRulesService _rules;
    private readonly IVoiceRoleGateway _gateway;
    private readonly ulong _connected;
    private readonly ILogger<VoiceRoleReconciler> _logger;
    private readonly ConcurrentDictionary<(ulong Guild, ulong Role), byte> _warnings = new();
    private readonly ConcurrentDictionary<(ulong Guild, ulong User), SemaphoreSlim> _memberGates = new();
    private readonly ActivityRulesService? _activities;
    private readonly PresenceSnapshotCache? _presence;
    private readonly ActivityOptions _activityOptions;
    private readonly bool _voiceEnabled;
    // REST can acknowledge a narrow mutation before its guild-member gateway echo arrives.
    private readonly ConcurrentDictionary<(ulong Guild, ulong User, ulong Role), (bool Present, DateTimeOffset Until)> _echoes = new();
    public VoiceRoleReconciler(IDbContextFactory<MrCleanDbContext> factory, VoiceRulesService rules,
        IVoiceRoleGateway gateway, IOptions<VoiceLoggingOptions> options, ILogger<VoiceRoleReconciler> logger,
        ActivityRulesService? activities = null, PresenceSnapshotCache? presence = null, IOptions<ActivityOptions>? activityOptions = null)
    { _factory = factory; _rules = rules; _gateway = gateway; _connected = options.Value.ConnectedRoleId; _logger = logger; _activities = activities; _presence = presence; _activityOptions = activityOptions?.Value ?? new ActivityOptions(); _voiceEnabled = options.Value.Enabled; }

    private async Task<bool> CanManageAsync(ulong guild, ulong user, ulong role, CancellationToken token)
    {
        var allowed = await _gateway.CanManageAsync(guild, user, role, token);
        if (!allowed && _warnings.TryAdd((guild, role), 0))
            _logger.LogWarning("Cannot manage activity role {RoleId} in guild {GuildId}; check role existence, permissions and hierarchy. Will retry.", role, guild);
        if (allowed) _warnings.TryRemove((guild, role), out _);
        return allowed;
    }

    private async Task<VoiceRoleMember> WithRecentAcknowledgementsAsync(ulong guild, VoiceRoleMember member, CancellationToken token)
    {
        var roles = member.Roles.ToHashSet();
        foreach (var pair in _echoes.Where(x => x.Key.Guild == guild && x.Key.User == member.UserId).ToArray())
        {
            if (DateTimeOffset.UtcNow >= pair.Value.Until || roles.Contains(pair.Key.Role) == pair.Value.Present ||
                !await _gateway.CanManageAsync(guild, member.UserId, pair.Key.Role, token))
            { _echoes.TryRemove(pair.Key, out _); continue; }
            if (pair.Value.Present) roles.Add(pair.Key.Role); else roles.Remove(pair.Key.Role);
        }
        return member with { Roles = roles.ToArray() };
    }
    private async Task AddRoleAsync(ulong guild, ulong user, ulong role, CancellationToken token)
    {
        await _gateway.AddAsync(guild, user, role, token);
        _echoes[(guild, user, role)] = (true, DateTimeOffset.UtcNow.AddSeconds(3));
    }
    private async Task RemoveRoleAsync(ulong guild, ulong user, ulong role, CancellationToken token)
    {
        await _gateway.RemoveAsync(guild, user, role, token);
        _echoes[(guild, user, role)] = (false, DateTimeOffset.UtcNow.AddSeconds(3));
    }

    public async Task ReconcileAsync(ulong guild, IEnumerable<ulong> observedUsers, CancellationToken token, bool includeOwned = true)
    {
        if (_connected == 0) return; // Fail closed if VC Roles protection is not configured.
        var users = observedUsers.Distinct().ToArray();
        if (includeOwned)
        {
            await using var lookup = await _factory.CreateDbContextAsync(token);
            users = users.Concat(await lookup.VoiceRoleOwnerships.Where(x => x.GuildId == guild).Select(x => x.UserId).ToListAsync(token))
                .Concat(await lookup.ActivityMemberStates.Where(x => x.GuildId == guild).Select(x => x.UserId).ToListAsync(token)).Distinct().ToArray();
        }
        foreach (var user in users) await ReconcileMemberAsync(guild, user, token);
    }

    public async Task<int> AdoptAsync(ulong guild, IEnumerable<ulong> users, ulong role, CancellationToken token)
    {
        if (_activities == null || !_activityOptions.ObservePresences || _connected == 0 || role == _connected || role == guild || role == 0)
            throw new ArgumentException("Presence capture/protection must be configured; Connected cannot be adopted.");
        var (rules, fallbacks, enabled) = await _activities.GetAsync(guild, token);
        if (!enabled || !rules.Any(x => x.Enabled && x.RoleId == role) && !fallbacks.Any(x => x.Enabled && x.RoleId == role))
            throw new ArgumentException("Enable management after cutover and configure this role before explicitly adopting its grants.");
        var count = 0;
        foreach (var user in users.Distinct())
        {
            var gate = _memberGates.GetOrAdd((guild, user), _ => new SemaphoreSlim(1)); await gate.WaitAsync(token);
            try
            {
                var member = await _gateway.MemberAsync(guild, user, token);
                if (member == null || !member.Roles.Contains(role) || !await CanManageAsync(guild, user, role, token)) continue;
                await using var db = await _factory.CreateDbContextAsync(token);
                var claim = await db.VoiceRoleOwnerships.FindAsync(new object[] { guild, user, role }, token);
                if (claim == null) { claim = new VoiceRoleOwnership { GuildId = guild, UserId = user, RoleId = role }; db.Add(claim); }
                claim.Confirmed = true;
                var contribution = await db.ActivityMemberStates.FindAsync(new object[] { guild, user }, token);
                if (contribution == null) { contribution = new ActivityMemberState { GuildId = guild, UserId = user }; db.Add(contribution); }
                contribution.RoleIdsJson = JsonSerializer.Serialize(JsonSerializer.Deserialize<ulong[]>(contribution.RoleIdsJson)!.Append(role).Distinct().OrderBy(x => x));
                await db.SaveChangesAsync(token); count++;
            }
            finally { gate.Release(); }
        }
        return count;
    }

    private async Task ReconcileMemberAsync(ulong guild, ulong user, CancellationToken token)
    {
        var gate = _memberGates.GetOrAdd((guild, user), _ => new SemaphoreSlim(1));
        await gate.WaitAsync(token);
        try
        {
            var rules = await _rules.ListAsync(guild, token);
            await using var db = await _factory.CreateDbContextAsync(token);
            var owned = await db.VoiceRoleOwnerships.Where(x => x.GuildId == guild && x.UserId == user).ToListAsync(token);
            // Never consume an event's old channel: read current membership inside the member lock.
            var member = await _gateway.MemberAsync(guild, user, token);
            if (member == null) return;
            if (member.Present) member = await WithRecentAcknowledgementsAsync(guild, member, token);
            else foreach (var key in _echoes.Keys.Where(x => x.Guild == guild && x.User == user).ToArray()) _echoes.TryRemove(key, out _);
            var desired = rules.Where(x => _voiceEnabled && x.Enabled && x.ChannelId == member.ChannelId)
                .SelectMany(x => x.RoleIds).ToHashSet();
            var pausedPresenceRoles = new HashSet<ulong>();
            if (_activities != null && _presence != null)
            {
                var (activityRules, fallbacks, enabled) = await _activities.GetAsync(guild, token);
                if (!enabled || !_activityOptions.ObservePresences)
                    pausedPresenceRoles.UnionWith(activityRules.Where(x => x.Enabled).Select(x => x.RoleId)
                        .Concat(fallbacks.Where(x => x.Enabled).Select(x => x.RoleId)));
                var previous = await db.ActivityMemberStates.FindAsync(new object[] { guild, user }, token);
                var retained = previous == null ? new HashSet<ulong>() : JsonSerializer.Deserialize<ulong[]>(previous.RoleIdsJson)!.ToHashSet();
                var observation = _presence.Get(guild, user);
                if (!member.Present)
                {
                    retained.Clear(); if (previous != null) db.Remove(previous); await db.SaveChangesAsync(token);
                }
                else if (enabled && _activityOptions.ObservePresences)
                {
                    var possible = activityRules.Where(x => x.Enabled).Select(x => x.RoleId)
                        .Concat(fallbacks.Where(x => x.Enabled).Select(x => x.RoleId)).ToHashSet();
                    retained = observation.Known ? ActivityMatching.Roles(observation.Activities, activityRules, fallbacks) :
                        retained.Concat(owned.Select(x => x.RoleId)).Concat(ActivityMatching.Roles(observation.Activities, activityRules, fallbacks)).Where(possible.Contains).ToHashSet();
                    // Minimal durable contribution. Missing presence never becomes confirmed inactivity.
                    if (retained.Count > 0)
                    {
                        if (previous == null) { previous = new ActivityMemberState { GuildId = guild, UserId = user }; db.Add(previous); }
                        previous.RoleIdsJson = JsonSerializer.Serialize(retained.OrderBy(x => x));
                    }
                    else if (previous != null) db.Remove(previous);
                    await db.SaveChangesAsync(token);
                }
                // Pausing neither removes nor re-adds presence-only grants. Keep the durable contribution
                // for resumption, but protect only roles that are actually present during the pause.
                if (!enabled || !_activityOptions.ObservePresences) retained.IntersectWith(member.Roles);
                desired.UnionWith(retained);
            }
            desired.Remove(_connected); desired.Remove(guild); desired.Remove(0);
            foreach (var claim in owned.Where(x => x.UserId == user).ToList())
            {
                if (member.Present && pausedPresenceRoles.Contains(claim.RoleId)) continue; // Do not compete with the current presence manager, even via a voice binding.
                if (claim.RoleId == _connected) continue; // Never touch VC Roles ownership.
                var present = member.Roles.Contains(claim.RoleId);
                if (!claim.Confirmed && present)
                {
                    // Ambiguous completion or another actor's grant: relinquish, never strip it.
                    db.Remove(claim); owned.Remove(claim); continue;
                }
                if (!desired.Contains(claim.RoleId))
                {
                    if (present)
                    {
                        if (!await CanManageAsync(guild, user, claim.RoleId, token)) continue;
                        await RemoveRoleAsync(guild, user, claim.RoleId, token);
                    }
                    db.Remove(claim); owned.Remove(claim);
                }
                else if (!present)
                {
                    if (!await CanManageAsync(guild, user, claim.RoleId, token))
                    { db.Remove(claim); owned.Remove(claim); continue; }
                    claim.Confirmed = false; await db.SaveChangesAsync(token);
                    await AddRoleAsync(guild, user, claim.RoleId, token);
                    claim.Confirmed = true;
                }
            }
            await db.SaveChangesAsync(token);
            foreach (var role in desired.Where(x => !pausedPresenceRoles.Contains(x) && !member.Roles.Contains(x) && !owned.Any(c => c.UserId == user && c.RoleId == x)))
            {
                if (!await CanManageAsync(guild, user, role, token)) continue;
                var claim = new VoiceRoleOwnership { GuildId = guild, UserId = user, RoleId = role };
                db.Add(claim); await db.SaveChangesAsync(token); // Durable intent before the role API call.
                owned.Add(claim);
                await AddRoleAsync(guild, user, role, token);
                claim.Confirmed = true; await db.SaveChangesAsync(token);
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Combined activity role reconciliation failed for member {UserId}; retrying later.", user);
        }
        finally { gate.Release(); }
    }
}
