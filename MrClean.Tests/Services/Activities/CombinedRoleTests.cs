using System;
using System.Collections.Concurrent;
using System.Linq;
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
public class CombinedRoleTests
{
    private Factory _factory = null!; private VoiceRulesService _voice = null!;
    private ActivityRulesService _activity = null!; private PresenceSnapshotCache _cache = null!;
    private Gateway _gateway = null!; private VoiceRoleReconciler _roles = null!;
    [SetUp] public void Setup()
    {
        _factory = new Factory(); _voice = new VoiceRulesService(_factory); _activity = new ActivityRulesService(_factory);
        _cache = new PresenceSnapshotCache(); _gateway = new Gateway(); _gateway.Members[3] = new VoiceRoleMember(3, 10, Array.Empty<ulong>()); _roles = Create();
    }
    private VoiceRoleReconciler Create() => new(_factory, _voice, _gateway,
        Options.Create(new VoiceLoggingOptions { Enabled = true, ConnectedRoleId = 999 }), NullLogger<VoiceRoleReconciler>.Instance,
        _activity, _cache, Options.Create(new ActivityOptions { ObservePresences = true }));
    private async Task Configure(bool voice = true)
    {
        if (voice) await _voice.SaveAsync(new VoiceChannelRule { GuildId = 1, ChannelId = 10, DestinationId = 100, RoleIds = new ulong[] { 20 } }, true, 999);
        await _activity.SaveAsync(new ActivityRule { GuildId = 1, Name = "Codenames", Type = ActivityType.Playing, RoleId = 20 }, true, 999);
        await _activity.EnableAsync(1, true);
    }
    private Task Tick() => _roles.ReconcileAsync(1, new ulong[] { 3 }, CancellationToken.None);
    private void Observe(params ReportedActivity[] activities) => _cache.ObserveGateway(1, 3, activities);
    private static ReportedActivity Game(string name = "Codenames") => new(name, ActivityType.Playing);

    [Test] public async Task VoiceOrPresenceRetainsRoleUntilBothEnd()
    {
        await Configure(); Observe(Game()); await Tick();
        _gateway.Members[3] = _gateway.Members[3] with { ChannelId = null }; await Tick();
        Assert.That(_gateway.Removes.Count, Is.Zero);
        Observe(); await Tick(); Assert.That(_gateway.Removes.Count, Is.EqualTo(1));
        Assert.That(_gateway.Adds.Count, Is.EqualTo(1));
    }
    [Test] public async Task PresenceEndingDoesNotRemoveVoiceContribution()
    {
        await Configure(); Observe(Game()); await Tick(); Observe(); await Tick();
        Assert.That(_gateway.Removes.Count, Is.Zero);
        _gateway.Members[3] = _gateway.Members[3] with { ChannelId = null }; await Tick();
        Assert.That(_gateway.Removes.Count, Is.EqualTo(1));
    }
    [Test] public async Task SimultaneousTrackedAndUntrackedGamesKeepGenericUntilLastUntrackedEnds()
    {
        await Configure(false); await _activity.FallbackAsync(new ActivityFallback { GuildId = 1, Type = ActivityType.Playing, RoleId = 30, Enabled = true }, 999);
        Observe(Game(), Game("Other"), Game("Another")); await Tick();
        Assert.That(_gateway.Members[3].Roles, Is.EquivalentTo(new ulong[] { 20, 30 }));
        Observe(Game(), Game("Another")); await Tick(); Assert.That(_gateway.Removes, Is.Empty);
        Observe(Game()); await Tick(); Assert.That(_gateway.Members[3].Roles, Is.EquivalentTo(new ulong[] { 20 }));
    }
    [Test] public async Task RestartUnknownPresenceKeepsDurableContributionUntilFreshEmpty()
    {
        await Configure(false); Observe(Game()); await Tick();
        _cache = new PresenceSnapshotCache(); _roles = Create();
        await Tick(); Assert.That(_gateway.Removes, Is.Empty);
        await using (var db = _factory.CreateDbContext())
        { var state = await db.ActivityMemberStates.SingleAsync(); Assert.That(state.RoleIdsJson, Does.Not.Contain("Codenames").And.Contain("20")); }
        Observe(); await Tick(); Assert.That(_gateway.Removes.Count, Is.EqualTo(1));
    }
    [Test] public async Task UnknownPresenceCanHoldRoleGrantedByVoiceIfBothTriggersMayRequireIt()
    {
        await Configure(); await Tick(); _gateway.Members[3] = _gateway.Members[3] with { ChannelId = null };
        await Tick(); Assert.That(_gateway.Removes, Is.Empty);
        Observe(); await Tick(); Assert.That(_gateway.Removes.Count, Is.EqualTo(1));
    }
    [Test] public async Task ConfigurationRemovalCleansOnlyOwnedGrantsAndNeverDeletesRoles()
    {
        await Configure(false); _gateway.Members[3] = _gateway.Members[3] with { Roles = new ulong[] { 88 } }; Observe(Game()); await Tick();
        var id = (await _activity.GetAsync(1)).Rules.Single().Id; await _activity.RemoveAsync(1, id); await Tick();
        Assert.That(_gateway.Members[3].Roles, Is.EquivalentTo(new ulong[] { 88 }));
    }
    [Test] public async Task PreexistingGrantNeedsExplicitAdoptionAndUnknownStateRemainsProtected()
    {
        await Configure(false); _gateway.Members[3] = _gateway.Members[3] with { Roles = new ulong[] { 20, 88 } };
        Observe(); await Tick(); Assert.That(_gateway.Removes, Is.Empty);
        _cache.Invalidate(1);
        Assert.That(await _roles.AdoptAsync(1, new ulong[] { 3 }, 20, CancellationToken.None), Is.EqualTo(1));
        await Tick(); Assert.That(_gateway.Removes, Is.Empty);
        Observe(); await Tick(); Assert.That(_gateway.Members[3].Roles, Is.EquivalentTo(new ulong[] { 88 }));
    }
    [Test] public async Task PauseDoesNotStripOrReAddPresenceOnlyRoles()
    {
        await Configure(false); Observe(Game()); await Tick(); await _activity.EnableAsync(1, false);
        Observe(); await Tick(); Assert.That(_gateway.Removes, Is.Empty);
        _gateway.Members[3] = _gateway.Members[3] with { Roles = Array.Empty<ulong>() };
        await Tick(); Assert.That(_gateway.Adds.Count, Is.EqualTo(1));
    }
    [Test] public async Task SameMemberConcurrentAndOutOfOrderNotificationsAreIdempotent()
    {
        await Configure(false); _cache.Observe(1, 3, new[] { Game() }, 10); _cache.Observe(1, 3, Array.Empty<ReportedActivity>(), 9);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Tick()));
        Assert.That(_gateway.Adds.Count, Is.EqualTo(1)); Assert.That(_gateway.Removes, Is.Empty);
    }
    [Test] public async Task DifferentMembersDoNotShareRoleMutationLocks()
    {
        await Configure(false); Observe(Game()); _gateway.Members[4] = new VoiceRoleMember(4, null, Array.Empty<ulong>());
        _cache.Observe(1, 4, new[] { Game() }); _gateway.BlockUser = 3;
        var first = _roles.ReconcileAsync(1, new ulong[] { 3 }, CancellationToken.None, false);
        await _gateway.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _roles.ReconcileAsync(1, new ulong[] { 4 }, CancellationToken.None, false).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(_gateway.Members[4].Roles, Does.Contain(20UL));
        _gateway.Release.SetResult(true); await first;
    }
    [Test] public async Task MissingPermissionsRetainIntentAndRetryWithoutTouchingUnrelatedRoles()
    {
        await Configure(false); Observe(Game()); _gateway.CanManage = false; await Tick(); Assert.That(_gateway.Adds, Is.Empty);
        _gateway.CanManage = true; await Tick(); Assert.That(_gateway.Adds.Count, Is.EqualTo(1));
    }
    [Test] public async Task DisabledFallbackCannotStripAnActiveVoiceRole()
    {
        await Configure();
        var fallback = new ActivityFallback { GuildId = 1, Type = ActivityType.Listening, RoleId = 20, Enabled = true };
        await _activity.FallbackAsync(fallback, 999); Observe(new ReportedActivity("Music", ActivityType.Listening)); await Tick();
        fallback.Enabled = false; await _activity.FallbackAsync(fallback, 999); await Tick();
        Assert.That(_gateway.Removes, Is.Empty);
        _gateway.Members[3] = _gateway.Members[3] with { ChannelId = null }; await Tick();
        Assert.That(_gateway.Removes.Count, Is.EqualTo(1));
    }

    [Test] public async Task PartialPositiveStartupSnapshotDoesNotEraseOtherLastKnownActivities()
    {
        await Configure(false); await _activity.SaveAsync(new ActivityRule { GuildId = 1, Name = "Second", RoleId = 21 }, true, 999);
        Observe(Game(), Game("Second")); await Tick(); _cache.Invalidate(1);
        _cache.SeedPositive(1, 3, new[] { Game() }); await Tick();
        Assert.That(_gateway.Members[3].Roles, Is.EquivalentTo(new ulong[] { 20, 21 }));
        Observe(Game()); await Tick(); Assert.That(_gateway.Members[3].Roles, Is.EquivalentTo(new ulong[] { 20 }));
    }

    [Test] public async Task ConfirmedGuildDepartureDropsOnlyTrackingStateWithoutRoleApiCalls()
    {
        await Configure(false); Observe(Game()); await Tick();
        _gateway.Members[3] = new VoiceRoleMember(3, null, Array.Empty<ulong>(), false); await Tick();
        await using var db = _factory.CreateDbContext();
        Assert.That(await db.ActivityMemberStates.CountAsync(), Is.Zero); Assert.That(await db.VoiceRoleOwnerships.CountAsync(), Is.Zero);
        Assert.That(_gateway.Removes, Is.Empty);
    }

    [Test] public async Task RestAcknowledgementBeforeGatewayEchoDoesNotDuplicateGrant()
    {
        await Configure(false); Observe(Game()); _gateway.DelayCacheEcho = true;
        await Tick(); await Tick();
        Assert.That(_gateway.Adds.Count, Is.EqualTo(1));
    }

    [Test] public async Task AdoptionRejectsUnconfiguredAndConnectedRoles()
    {
        await Configure(false);
        Assert.ThrowsAsync<ArgumentException>(() => _roles.AdoptAsync(1, new ulong[] { 3 }, 999, CancellationToken.None));
        Assert.ThrowsAsync<ArgumentException>(() => _roles.AdoptAsync(1, new ulong[] { 3 }, 88, CancellationToken.None));
    }
    [Test] public async Task SwitchingTrackedToUntrackedThenEndingRemovesBothRolesPromptly()
    {
        await Configure(false);
        await _activity.FallbackAsync(new ActivityFallback { GuildId = 1, Type = ActivityType.Playing, RoleId = 30, Enabled = true }, 999);
        Observe(Game()); await Tick();
        Observe(Game("Untracked")); await Tick();
        Assert.That(_gateway.Members[3].Roles, Is.EquivalentTo(new ulong[] { 30 }));
        Observe(); await Tick();
        Assert.That(_gateway.Members[3].Roles, Is.Empty);
        Assert.That(_gateway.Removes, Is.EquivalentTo(new ulong[] { 20, 30 }));
    }

    [Test] public async Task PreMidDisappearingWhileAnotherActivityRemainsRemovesOnlyItsRole()
    {
        await Configure(false);
        await _activity.SaveAsync(new ActivityRule { GuildId = 1, Name = "YouTube", Type = ActivityType.Watching, RoleId = 21 }, true, 999);
        Observe(Game(), new ReportedActivity("YouTube", ActivityType.Watching)); await Tick();
        Observe(Game()); await Tick();
        Assert.That(_gateway.Members[3].Roles, Is.EquivalentTo(new ulong[] { 20 }));
        Observe(); await Tick(); Assert.That(_gateway.Members[3].Roles, Is.Empty);
    }

    [Test] public async Task ReconnectUnknownRetainsButFreshOfflineEmptyClearsAndReturnReAdds()
    {
        await Configure(false); Observe(Game()); await Tick();
        _cache.Invalidate(1); await Tick(); Assert.That(_gateway.Removes, Is.Empty);
        // Offline status does not change the meaning of the fresh gateway activity list.
        Observe(); await Tick(); Assert.That(_gateway.Members[3].Roles, Is.Empty);
        Observe(Game()); await Tick(); Assert.That(_gateway.Members[3].Roles, Is.EquivalentTo(new ulong[] { 20 }));
        Assert.That(_gateway.Adds.Count, Is.EqualTo(2));
    }

    [Test] public async Task ObservationOnlyFreshEmptyNeverClaimsOrRemovesTuxedoManGrants()
    {
        await Configure(false); await _activity.EnableAsync(1, false);
        _gateway.Members[3] = _gateway.Members[3] with { Roles = new ulong[] { 20, 88 } };
        Observe(Game()); await Tick(); Observe(); await Tick();
        Assert.That(_gateway.Adds, Is.Empty); Assert.That(_gateway.Removes, Is.Empty);
        await using var db = _factory.CreateDbContext();
        Assert.That(await db.VoiceRoleOwnerships.CountAsync(), Is.Zero);
    }

    [Test] public async Task ObservationOnlyProtectsSharedVoiceBindingFromCompetingWithTuxedoMan()
    {
        await Configure(); Observe(Game()); await Tick();
        await _activity.EnableAsync(1, false);
        _gateway.Members[3] = _gateway.Members[3] with { ChannelId = null };
        Observe(); await Tick(); Assert.That(_gateway.Removes, Is.Empty);
        // TuxedoMan removes its role; even an active voice binding must not re-add it while paused.
        _gateway.Members[3] = _gateway.Members[3] with { ChannelId = 10, Roles = Array.Empty<ulong>() };
        await Tick(); Assert.That(_gateway.Adds.Count, Is.EqualTo(1));
        // Explicit cutover resumes normal combined voice/presence ownership.
        await _activity.EnableAsync(1, true); await Tick();
        Assert.That(_gateway.Adds.Count, Is.EqualTo(2));
    }

    [Test] public async Task ActivityEndingDuringInFlightGrantConvergesToLatestState()
    {
        await Configure(false); Observe(Game()); _gateway.BlockUser = 3;
        var starting = Tick(); await _gateway.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Observe(); var ending = Tick();
        _gateway.Release.SetResult(true); await Task.WhenAll(starting, ending);
        Assert.That(_gateway.Members[3].Roles, Is.Empty);
        Assert.That(_gateway.Adds.Count, Is.EqualTo(1)); Assert.That(_gateway.Removes.Count, Is.EqualTo(1));
    }

    [Test] public async Task EndedRoleWithMissingPermissionsRetainsOwnershipAndRetriesRemoval()
    {
        await Configure(false); Observe(Game()); await Tick();
        _gateway.CanManage = false; Observe(); await Tick();
        Assert.That(_gateway.Removes, Is.Empty);
        await using (var db = _factory.CreateDbContext())
            Assert.That(await db.VoiceRoleOwnerships.CountAsync(), Is.EqualTo(1));
        _gateway.CanManage = true; await Tick(); await Tick();
        Assert.That(_gateway.Removes.Count, Is.EqualTo(1));
    }

    private class Factory : IDbContextFactory<MrCleanDbContext>
    {
        private readonly DbContextOptions<MrCleanDbContext> _options = new DbContextOptionsBuilder<MrCleanDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public MrCleanDbContext CreateDbContext() => new(_options);
    }
    private class Gateway : IVoiceRoleGateway
    {
        public ConcurrentDictionary<ulong, VoiceRoleMember> Members = new();
        public ConcurrentBag<ulong> Adds = new(), Removes = new(); public bool CanManage = true;
        public ulong BlockUser; public bool DelayCacheEcho;
        public TaskCompletionSource<bool> Blocked = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<VoiceRoleMember?> MemberAsync(ulong guild, ulong user, CancellationToken token) => Task.FromResult(Members.TryGetValue(user, out var member) ? member : null);
        public Task<bool> CanManageAsync(ulong guild, ulong user, ulong role, CancellationToken token) => Task.FromResult(CanManage);
        public async Task AddAsync(ulong guild, ulong user, ulong role, CancellationToken token)
        {
            if (user == BlockUser) { Blocked.TrySetResult(true); await Release.Task; }
            Adds.Add(role); if (DelayCacheEcho) return; Members[user] = Members[user] with { Roles = Members[user].Roles.Append(role).Distinct().ToArray() };
        }
        public Task RemoveAsync(ulong guild, ulong user, ulong role, CancellationToken token)
        { Removes.Add(role); Members[user] = Members[user] with { Roles = Members[user].Roles.Where(x => x != role).ToArray() }; return Task.CompletedTask; }
    }
}
