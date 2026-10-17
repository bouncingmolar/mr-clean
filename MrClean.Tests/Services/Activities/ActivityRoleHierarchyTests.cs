using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MrClean.Configuration;
using MrClean.Data;
using MrClean.Models;
using MrClean.Services.Activities;
using MrClean.Services.VoiceRules;
using NUnit.Framework;

namespace MrClean.Tests.Services.Activities;

public class ActivityRoleHierarchyTests
{
    [TestCase(true)] [TestCase(false)]
    public async Task HigherAdministratorReceivesAndLosesOwnedLowerRoleFromVoiceOrPresence(bool voice)
    {
        var f = new Fixture(); await f.Configure();
        Assert.That(f.Gateway.HighRole.Position, Is.GreaterThan(Gateway.BotHierarchy));
        Assert.That(f.Gateway.Target.GuildPermissions.Administrator, Is.True);
        f.Presence.ObserveGateway(1, 3, voice ? Array.Empty<ReportedActivity>() : new[] { new ReportedActivity("Gartic.io", ActivityType.Playing) });
        f.Gateway.Channel = voice ? 10UL : null;
        await f.Tick(); Assert.That(f.Gateway.Roles, Does.Contain(20UL));
        f.Gateway.Channel = null; f.Presence.ObserveGateway(1, 3, Array.Empty<ReportedActivity>());
        await f.Tick();
        Assert.That(f.Gateway.Roles, Is.EquivalentTo(new ulong[] { 247, 88 }));
        Assert.That(f.Gateway.Added, Is.EqualTo(new ulong[] { 20 }));
        Assert.That(f.Gateway.Removed, Is.EqualTo(new ulong[] { 20 }));
    }

    [Test] public async Task ReviewedHigherAdministratorAdoptionRemovesStaleGameAndKeepsCurrentGame()
    {
        var f = new Fixture(); await f.Configure();
        await f.Activities.SaveAsync(new ActivityRule { GuildId = 1, Name = "Codenames", RoleId = 21 }, true, 999);
        f.Gateway.Roles.Add(20); // Existing TuxedoMan/manual grant is not automatically owned.
        f.Presence.ObserveGateway(1, 3, new[] { new ReportedActivity("Codenames", ActivityType.Playing) });
        await f.Tick(); Assert.That(f.Gateway.Roles, Does.Contain(20UL).And.Contain(21UL));
        Assert.That(f.Gateway.Removed, Is.Empty);
        Assert.That(await f.Reconciler.AdoptAsync(1, new ulong[] { 3 }, 20, CancellationToken.None), Is.EqualTo(1));
        await f.Tick();
        Assert.That(f.Gateway.Roles, Is.EquivalentTo(new ulong[] { 247, 88, 21 }));
        Assert.That(f.Gateway.Removed, Is.EqualTo(new ulong[] { 20 }));
    }

    [TestCase(true)] [TestCase(false)]
    public async Task HigherAdministratorSharedRoleSurvivesEitherTriggerEndingFirst(bool voiceFirst)
    {
        var f = new Fixture(); await f.Configure(); f.Gateway.Channel = 10;
        f.Presence.ObserveGateway(1, 3, new[] { new ReportedActivity("Gartic.io", ActivityType.Playing) });
        await f.Tick();
        if (voiceFirst) f.Gateway.Channel = null;
        else f.Presence.ObserveGateway(1, 3, Array.Empty<ReportedActivity>());
        await f.Tick(); Assert.That(f.Gateway.Removed, Is.Empty);
        f.Gateway.Channel = null; f.Presence.ObserveGateway(1, 3, Array.Empty<ReportedActivity>());
        await f.Tick(); Assert.That(f.Gateway.Removed, Is.EqualTo(new ulong[] { 20 }));
        Assert.That(f.Gateway.Roles, Does.Contain(247UL).And.Contain(88UL));
    }

    [TestCase(245)] [TestCase(246)]
    public async Task AtOrAboveBotRoleCannotBeAssignedOrAdopted(int position)
    {
        var f = new Fixture(); await f.Configure(); f.Gateway.RolePosition = position; f.Gateway.Channel = 10;
        await f.Tick(); Assert.That(f.Gateway.Added, Is.Empty);
        f.Gateway.Roles.Add(20);
        Assert.That(await f.Reconciler.AdoptAsync(1, new ulong[] { 3 }, 20, CancellationToken.None), Is.Zero);
        Assert.That(f.Gateway.Removed, Is.Empty);
    }

    [TestCase(GuildPermission.Administrator)] [TestCase(GuildPermission.ManageRoles)] [TestCase(GuildPermission.ManageGuild)]
    public async Task PrivilegedRoleIsProtectedForAssignmentAndAdoption(GuildPermission permission)
    {
        var f = new Fixture(); await f.Configure(); f.Gateway.RolePermissions = new GuildPermissions((ulong)permission); f.Gateway.Channel = 10;
        await f.Tick(); Assert.That(f.Gateway.Added, Is.Empty);
        f.Gateway.Roles.Add(20);
        Assert.That(await f.Reconciler.AdoptAsync(1, new ulong[] { 3 }, 20, CancellationToken.None), Is.Zero);
    }

    [Test] public void IntegrationAndEveryoneRolesAndMissingBotPermissionRemainProtected()
    {
        var g = new Gateway();
        Assert.That(g.Check(Role(20, 10, managed: true)), Is.False);
        Assert.That(g.Check(Role(1, 0)), Is.False);
        g.ManageRoles = false;
        Assert.That(g.Check(Role(20, 10)), Is.False);
    }

    [Test] public void ExistingOwnerExclusionRemainsExplicitApplicationPolicy()
    {
        var g = new Gateway();
        Assert.That(DiscordVoiceRoleGateway.CanManageRole(1, 3, g.Target, g.Bot, Gateway.BotHierarchy, Role(20, 139)), Is.False);
    }

    [Test] public async Task CleanupWaitsIfOwnedRoleMovesAboveBotAndRetriesWhenManageable()
    {
        var f = new Fixture(); await f.Configure(); f.Gateway.Channel = 10;
        f.Presence.ObserveGateway(1, 3, Array.Empty<ReportedActivity>());
        await f.Tick(); f.Gateway.Channel = null; f.Gateway.RolePosition = 245;
        await f.Tick(); Assert.That(f.Gateway.Removed, Is.Empty);
        await using (var db = f.Factory.CreateDbContext()) Assert.That(await db.VoiceRoleOwnerships.CountAsync(), Is.EqualTo(1));
        f.Gateway.RolePosition = 139; await f.Tick(); await f.Tick();
        Assert.That(f.Gateway.Removed, Is.EqualTo(new ulong[] { 20 }));
    }

    [Test] public async Task UnknownPresenceRetainsReviewedAdoptionAndConnectedCannotBeAdopted()
    {
        var f = new Fixture(); await f.Configure(); f.Gateway.Roles.Add(20);
        Assert.That(await f.Reconciler.AdoptAsync(1, new ulong[] { 3 }, 20, CancellationToken.None), Is.EqualTo(1));
        await f.Tick(); Assert.That(f.Gateway.Removed, Is.Empty);
        Assert.ThrowsAsync<ArgumentException>(() => f.Reconciler.AdoptAsync(1, new ulong[] { 3 }, 999, CancellationToken.None));
        Assert.ThrowsAsync<ArgumentException>(() => f.Reconciler.AdoptAsync(1, new ulong[] { 3 }, 88, CancellationToken.None));
    }

    private sealed class Fixture
    {
        public Factory Factory = new(); public Gateway Gateway = new(); public PresenceSnapshotCache Presence = new();
        public ActivityRulesService Activities; public VoiceRulesService Voice; public VoiceRoleReconciler Reconciler;
        public Fixture()
        {
            Activities = new ActivityRulesService(Factory); Voice = new VoiceRulesService(Factory);
            Reconciler = new VoiceRoleReconciler(Factory, Voice, Gateway, Options.Create(new VoiceLoggingOptions { Enabled = true, ConnectedRoleId = 999 }),
                NullLogger<VoiceRoleReconciler>.Instance, Activities, Presence, Options.Create(new ActivityOptions { ObservePresences = true }));
        }
        public async Task Configure()
        {
            await Voice.SaveAsync(new VoiceChannelRule { GuildId = 1, ChannelId = 10, DestinationId = 100, RoleIds = new ulong[] { 20 } }, true, 999);
            await Activities.SaveAsync(new ActivityRule { GuildId = 1, Name = "Gartic.io", RoleId = 20 }, true, 999);
            await Activities.EnableAsync(1, true);
        }
        public Task Tick() => Reconciler.ReconcileAsync(1, new ulong[] { 3 }, CancellationToken.None);
    }
    private sealed class Factory : IDbContextFactory<MrCleanDbContext>
    {
        private readonly DbContextOptions<MrCleanDbContext> _options = new DbContextOptionsBuilder<MrCleanDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public MrCleanDbContext CreateDbContext() => new(_options);
    }
    private sealed class Gateway : IVoiceRoleGateway
    {
        public const int BotHierarchy = 245;
        public HashSet<ulong> Roles = new() { 247, 88 }; public List<ulong> Added = new(), Removed = new(); public ulong? Channel;
        public int RolePosition = 139; public GuildPermissions RolePermissions = new(0); public bool ManageRoles = true;
        public IRole HighRole = Role(247, 247, new GuildPermissions((ulong)GuildPermission.Administrator));
        public IGuildUser Target => Stub<IGuildUser>(name => name switch { "Id" => 3UL, "RoleIds" => Roles.ToArray(), "GuildPermissions" => HighRole.Permissions, _ => throw new NotSupportedException(name) });
        public IGuildUser Bot => Stub<IGuildUser>(name => name switch { "Id" => 4UL, "GuildPermissions" => new GuildPermissions(ManageRoles ? (ulong)GuildPermission.ManageRoles : 0), _ => throw new NotSupportedException(name) });
        public bool Check(IRole role) => DiscordVoiceRoleGateway.CanManageRole(1, 2, Target, Bot, BotHierarchy, role);
        public Task<VoiceRoleMember?> MemberAsync(ulong guild, ulong user, CancellationToken token) => Task.FromResult<VoiceRoleMember?>(new VoiceRoleMember(user, Channel, Roles.ToArray()));
        public Task<bool> CanManageAsync(ulong guild, ulong user, ulong role, CancellationToken token) => Task.FromResult(Check(Role(role, RolePosition, RolePermissions)));
        public Task AddAsync(ulong guild, ulong user, ulong role, CancellationToken token) { Roles.Add(role); Added.Add(role); return Task.CompletedTask; }
        public Task RemoveAsync(ulong guild, ulong user, ulong role, CancellationToken token) { Roles.Remove(role); Removed.Add(role); return Task.CompletedTask; }
    }
    // Built-in .NET interface proxies avoid adding a mocking dependency or constructing SDK caches.
    private static IRole Role(ulong id, int position, GuildPermissions permissions = default, bool managed = false) => Stub<IRole>(name => name switch
    { "Id" => id, "Position" => position, "Permissions" => permissions, "IsManaged" => managed, _ => throw new NotSupportedException(name) });
    private static T Stub<T>(Func<string, object?> getter) where T : class
    { var value = DispatchProxy.Create<T, PropertyProxy>(); ((PropertyProxy)(object)value).Getter = getter; return value; }
    public class PropertyProxy : DispatchProxy
    {
        public Func<string, object?> Getter = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Getter(method!.Name.Replace("get_", ""));
    }
}
