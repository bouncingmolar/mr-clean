using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Microsoft.EntityFrameworkCore;
using MrClean.Commands.Activities;
using MrClean.Data;
using MrClean.Models;
using MrClean.Services.Activities;
using NUnit.Framework;
namespace MrClean.Tests.Services.Activities;
public class ActivityConfigurationTests
{
    [Test] public async Task TrackEditUntrackAreGuildScopedAndAliasesCanShareARole()
    {
        var factory = new Factory(); var service = new ActivityRulesService(factory);
        var rule = new ActivityRule { GuildId = 1, Name = "Codenames", Type = ActivityType.Playing, RoleId = 20 };
        await service.SaveAsync(rule, true, 999);
        await service.SaveAsync(new ActivityRule { GuildId = 1, Name = "Codenames Online", RoleId = 20 }, true, 999);
        Assert.That((await service.GetAsync(1)).Rules.Count, Is.EqualTo(2));
        Assert.That((await service.GetAsync(2)).Rules, Is.Empty);
        Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(new ActivityRule { GuildId = 1, Name = "Codenames", RoleId = 20 }, true, 999));
        rule.ApplicationId = 77; rule.RoleId = 21; await service.SaveAsync(rule, false, 999);
        Assert.That((await service.GetAsync(1)).Rules.First().RoleId, Is.EqualTo(21));
        Assert.ThrowsAsync<ArgumentException>(() => service.RemoveAsync(2, rule.Id));
        await service.RemoveAsync(1, rule.Id); Assert.That((await service.GetAsync(1)).Rules.Count, Is.EqualTo(1));
        Assert.That((await service.GetAsync(1)).Enabled, Is.False);
    }
    [Test] public async Task FallbackAndManagementUpdatesAreIdempotent()
    {
        var service = new ActivityRulesService(new Factory());
        var fallback = new ActivityFallback { GuildId = 1, Type = ActivityType.Playing, RoleId = 20 };
        await service.FallbackAsync(fallback, 999); fallback.Enabled = true; await service.FallbackAsync(fallback, 999);
        await service.EnableAsync(1, true); await service.EnableAsync(1, true);
        var config = await service.GetAsync(1); Assert.That(config.Fallbacks.Count, Is.EqualTo(1)); Assert.That(config.Enabled, Is.True);
        Assert.Throws<ArgumentException>(() => ActivityRulesService.Validate(1, "game", ActivityType.Playing, 999, 999, null));
        Assert.Throws<ArgumentException>(() => ActivityRulesService.ValidateMatch("game", ActivityType.Playing, 0));
        Assert.Throws<ArgumentException>(() => ActivityRulesService.ValidateMatch("game", ActivityType.CustomStatus, null));
    }
    [Test] public void NativeCommandStructureIsGuildOnlyAndHasRoleAndUserPickers()
    {
        var command = ActivityCommand.Build();
        Assert.That(command.DefaultMemberPermissions.Value, Is.EqualTo(GuildPermission.ManageGuild));
        var subs = command.Options.Value.ToArray();
        Assert.That(subs.Select(x => x.Name), Is.EquivalentTo(new[] { "track", "edit", "untrack", "list", "fallback", "enable", "inspect", "adopt", "confirm-empty" }));
        Assert.That(subs.Single(x => x.Name == "track").Options.Single(x => x.Name == "role").Type, Is.EqualTo(ApplicationCommandOptionType.Role));
        Assert.That(subs.Single(x => x.Name == "inspect").Options.Single().Type, Is.EqualTo(ApplicationCommandOptionType.User));
        foreach (var sub in subs)
        {
            var optionalSeen = false;
            foreach (var option in sub.Options?.AsEnumerable() ?? Enumerable.Empty<ApplicationCommandOptionProperties>())
            { if (!option.IsRequired.GetValueOrDefault()) optionalSeen = true; else Assert.That(optionalSeen, Is.False, "Discord requires required options first."); }
        }
    }
    [Test] public async Task ExplicitRoleCreationReusesExistingAndConcurrentRequestsCreateOnlyOnce()
    {
        var catalog = new Catalog(); catalog.Roles.Add(new ActivityRoleCandidate(20, "Other Games", true));
        var resolver = new ActivityRoleResolver(catalog);
        Assert.That(await resolver.ResolveAsync(1, null, "Other Games", 999), Is.EqualTo(20));
        var ids = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => resolver.ResolveAsync(1, null, "Playing Codenames", 999)));
        Assert.That(ids.Distinct().Count(), Is.EqualTo(1)); Assert.That(catalog.Created, Is.EqualTo(1));
    }
    [Test] public void AmbiguousUnsafeOrProtectedRolesNeverCauseAutomaticDuplicates()
    {
        var catalog = new Catalog(); var resolver = new ActivityRoleResolver(catalog);
        catalog.Roles.Add(new ActivityRoleCandidate(20, "game", true)); catalog.Roles.Add(new ActivityRoleCandidate(21, "game", true));
        Assert.ThrowsAsync<ArgumentException>(() => resolver.ResolveAsync(1, null, "game", 999));
        catalog.Roles.Add(new ActivityRoleCandidate(22, "unsafe", false));
        Assert.ThrowsAsync<ArgumentException>(() => resolver.ResolveAsync(1, 22, null, 999));
        catalog.Roles.Add(new ActivityRoleCandidate(999, "connected", true));
        Assert.ThrowsAsync<ArgumentException>(() => resolver.ResolveAsync(1, 999, null, 999));
        Assert.That(catalog.Created, Is.Zero);
    }
    [Test] public void UncertainCreationDoesNotBlindlyRetryAndCreateDuplicates()
    {
        var catalog = new Catalog { FailCreate = true }; var resolver = new ActivityRoleResolver(catalog);
        Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(1, null, "game", 999));
        catalog.FailCreate = false;
        Assert.ThrowsAsync<ArgumentException>(() => resolver.ResolveAsync(1, null, "game", 999));
        Assert.That(catalog.Created, Is.EqualTo(1));
    }
    private class Factory : IDbContextFactory<MrCleanDbContext>
    {
        private readonly DbContextOptions<MrCleanDbContext> _options = new DbContextOptionsBuilder<MrCleanDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public MrCleanDbContext CreateDbContext() => new(_options);
    }
    private class Catalog : IActivityRoleCatalog
    {
        public List<ActivityRoleCandidate> Roles = new(); public int Created; public bool FailCreate;
        public Task<IReadOnlyList<ActivityRoleCandidate>> ListAsync(ulong guild) => Task.FromResult<IReadOnlyList<ActivityRoleCandidate>>(Roles.ToArray());
        public Task<ActivityRoleCandidate> CreateAsync(ulong guild, string name)
        {
            Created++; if (FailCreate) throw new InvalidOperationException("Unknown API outcome");
            var role = new ActivityRoleCandidate((ulong)(Created + 100), name, true); Roles.Add(role); return Task.FromResult(role);
        }
    }
}
