using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MrClean.Configuration;
using MrClean.Data;
using MrClean.Models;

namespace MrClean.Services.VoiceRules;

public record VoiceRoleMember(ulong UserId, ulong? ChannelId, IReadOnlyCollection<ulong> Roles);
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
    private readonly HashSet<(ulong Guild, ulong Role)> _warnings = new();
    private readonly SemaphoreSlim _gate = new(1);
    public VoiceRoleReconciler(IDbContextFactory<MrCleanDbContext> factory, VoiceRulesService rules,
        IVoiceRoleGateway gateway, IOptions<VoiceLoggingOptions> options, ILogger<VoiceRoleReconciler> logger)
    { _factory = factory; _rules = rules; _gateway = gateway; _connected = options.Value.ConnectedRoleId; _logger = logger; }

    private async Task<bool> CanManageAsync(ulong guild, ulong user, ulong role, CancellationToken token)
    {
        var allowed = await _gateway.CanManageAsync(guild, user, role, token);
        if (!allowed && _warnings.Add((guild, role)))
            _logger.LogWarning("Cannot manage activity role {RoleId} in guild {GuildId}; check role existence, permissions and hierarchy. Will retry.", role, guild);
        if (allowed) _warnings.Remove((guild, role));
        return allowed;
    }

    public async Task ReconcileAsync(ulong guild, IEnumerable<ulong> observedUsers, CancellationToken token)
    {
        if (_connected == 0) return; // Fail closed if VC Roles protection is not configured.
        await _gate.WaitAsync(token);
        try
        {
            var rules = await _rules.ListAsync(guild, token);
            await using var db = await _factory.CreateDbContextAsync(token);
            var owned = await db.VoiceRoleOwnerships.Where(x => x.GuildId == guild).ToListAsync(token);
            foreach (var user in observedUsers.Concat(owned.Select(x => x.UserId)).Distinct().ToArray())
            {
                try
                {
                    // Always read current membership; never act on the channel from an old gateway event.
                    var member = await _gateway.MemberAsync(guild, user, token);
                    if (member == null) continue; // Unavailable users are retried; do not infer absence.
                    var desired = rules.Where(x => x.Enabled && x.ChannelId == member.ChannelId)
                        .SelectMany(x => x.RoleIds).Where(x => x != _connected && x != guild && x != 0).ToHashSet();
                    foreach (var claim in owned.Where(x => x.UserId == user).ToList())
                    {
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
                                await _gateway.RemoveAsync(guild, user, claim.RoleId, token);
                            }
                            db.Remove(claim); owned.Remove(claim);
                        }
                        else if (!present)
                        {
                            if (!await CanManageAsync(guild, user, claim.RoleId, token))
                            { db.Remove(claim); owned.Remove(claim); continue; }
                            claim.Confirmed = false; await db.SaveChangesAsync(token);
                            await _gateway.AddAsync(guild, user, claim.RoleId, token);
                            claim.Confirmed = true;
                        }
                    }
                    await db.SaveChangesAsync(token);
                    foreach (var role in desired.Where(x => !member.Roles.Contains(x) && !owned.Any(c => c.UserId == user && c.RoleId == x)))
                    {
                        if (!await CanManageAsync(guild, user, role, token)) continue;
                        var claim = new VoiceRoleOwnership { GuildId = guild, UserId = user, RoleId = role };
                        db.Add(claim); await db.SaveChangesAsync(token); // Durable intent before the role API call.
                        owned.Add(claim);
                        await _gateway.AddAsync(guild, user, role, token);
                        claim.Confirmed = true; await db.SaveChangesAsync(token);
                    }
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Activity role reconciliation failed for member {UserId}; retrying later.", user);
                    // A failed change tracker/uncertain commit must not affect the next member.
                    return;
                }
            }
        }
        finally { _gate.Release(); }
    }
}
