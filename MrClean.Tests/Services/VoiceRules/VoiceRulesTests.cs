using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MrClean.Commands.Voice;
using MrClean.Configuration;
using MrClean.Data;
using MrClean.Models;
using MrClean.Services.VoiceRules;
using NUnit.Framework;

namespace MrClean.Tests.Services.VoiceRules;

public class VoiceRulesTests
{
    private Factory _factory = null!;
    private VoiceRulesService _rules = null!;
    private FakeGateway _gateway = null!;
    private VoiceRoleReconciler _reconciler = null!;
    private const ulong Connected = 894787914599370803;
    [SetUp] public void Setup()
    {
        _factory = new Factory(); _rules = new VoiceRulesService(_factory); _gateway = new FakeGateway();
        _gateway.Member = new VoiceRoleMember(3, 10, Array.Empty<ulong>());
        _reconciler = new VoiceRoleReconciler(_factory, _rules, _gateway,
            Options.Create(new VoiceLoggingOptions { Enabled = true, ConnectedRoleId = Connected }), NullLogger<VoiceRoleReconciler>.Instance);
    }
    private static VoiceChannelRule Rule(ulong channel = 10, params ulong[] roles) => new()
    { GuildId = 1, ChannelId = channel, DestinationId = 99, ActivityName = "Codenames", RoleIds = roles };
    private Task Reconcile() => _reconciler.ReconcileAsync(1, new ulong[] { 3 }, CancellationToken.None);
    private Task Save(VoiceChannelRule rule, bool add = true) => _rules.SaveAsync(rule, add, Connected);

    [Test] public async Task AddEditListRemoveAreGuildScopedAndPreserveOwnershipForCleanup()
    {
        var rule = Rule(10, 20); await Save(rule);
        Assert.That((await _rules.ListAsync(2)), Is.Empty);
        var loaded = (await _rules.ListAsync(1)).Single();
        loaded.Wording = "Playing {activity}"; loaded.LogJoin = false; await Save(loaded, false);
        Assert.That((await _rules.ListAsync(1)).Single().Activity("ignored"), Is.EqualTo("Playing Codenames"));
        await Reconcile(); await _rules.RemoveAsync(1, 10);
        await using (var db = _factory.CreateDbContext()) Assert.That(await db.VoiceRoleOwnerships.CountAsync(), Is.EqualTo(1));
        await Reconcile();
        Assert.That(_gateway.Removed, Is.EqualTo(new ulong[] { 20 }));
        Assert.That(await _rules.ListAsync(1), Is.Empty);
    }

    [Test] public async Task DuplicateAddAndMissingEditOrRemoveReject()
    {
        await Save(Rule());
        Assert.ThrowsAsync<ArgumentException>(() => Save(Rule()));
        Assert.ThrowsAsync<ArgumentException>(() => Save(Rule(11), false));
        Assert.ThrowsAsync<ArgumentException>(() => _rules.RemoveAsync(2, 10));
    }

    [TestCase(false, false, false)] [TestCase(true, false, true)] [TestCase(false, true, true)]
    public void ServerSidePermissionGate(bool manage, bool admin, bool allowed)
    {
        if (allowed) Assert.DoesNotThrow(() => VoiceRulesService.RequireAdministrator(manage, admin));
        else Assert.Throws<UnauthorizedAccessException>(() => VoiceRulesService.RequireAdministrator(manage, admin));
    }

    [Test] public void InvalidIdsAndProtectedRolesReject()
    {
        var rule = Rule(0); Assert.Throws<ArgumentException>(() => VoiceRulesService.Validate(rule, Connected));
        foreach (var id in new ulong[] { 0, 1, Connected })
        { rule = Rule(10, id); Assert.Throws<ArgumentException>(() => VoiceRulesService.Validate(rule, Connected)); }
        rule = Rule(); rule.DestinationId = 0; Assert.Throws<ArgumentException>(() => VoiceRulesService.Validate(rule, Connected));
        rule = Rule(); rule.Wording = "line\nbreak"; Assert.Throws<ArgumentException>(() => VoiceRulesService.Validate(rule, Connected));
    }

    [Test] public void NativePickerCommandContainsAllSubcommandsAndPermissionDefault()
    {
        var command = VoiceCommand.Build();
        Assert.That(command.Options.Value.Select(x => x.Name), Is.EqualTo(new[] { "add", "edit", "list", "remove" }));
        Assert.That(command.DefaultMemberPermissions.Value, Is.EqualTo(GuildPermission.ManageGuild));
        var add = command.Options.Value.First();
        Assert.That(add.Options.First(x => x.Name == "channel").Type, Is.EqualTo(ApplicationCommandOptionType.Channel));
        Assert.That(add.Options.First(x => x.Name == "add-role").Type, Is.EqualTo(ApplicationCommandOptionType.Role));
    }

    [Test] public async Task GrantsAreIdempotentAndDepartureRemovesOnlyOwnedRoles()
    {
        await Save(Rule(10, 20));
        await Reconcile(); await Reconcile();
        Assert.That(_gateway.Added, Is.EqualTo(new ulong[] { 20 }));
        _gateway.Member = _gateway.Member! with { ChannelId = null };
        await Reconcile(); await Reconcile();
        Assert.That(_gateway.Removed, Is.EqualTo(new ulong[] { 20 }));
    }

    [Test] public async Task SharedRoleSurvivesSwitchWithoutRemoveOrRegrant()
    {
        await Save(Rule(10, 20, 21)); await Save(Rule(11, 20, 22));
        await Reconcile(); _gateway.Member = _gateway.Member! with { ChannelId = 11 };
        await Reconcile();
        Assert.That(_gateway.Added, Is.EqualTo(new ulong[] { 20, 21, 22 }));
        Assert.That(_gateway.Removed, Is.EqualTo(new ulong[] { 21 }));
    }

    [Test] public async Task PreexistingManualRolesNeverBecomeOwnedOrGetRemoved()
    {
        _gateway.Member = _gateway.Member! with { Roles = new ulong[] { 20, Connected, 88 } };
        await Save(Rule(10, 20)); await Reconcile();
        _gateway.Member = _gateway.Member with { ChannelId = null }; await Reconcile();
        Assert.That(_gateway.Added, Is.Empty); Assert.That(_gateway.Removed, Is.Empty);
    }

    [Test] public async Task RuleEditDisableAndDeletedChannelReconcileAgainstCurrentMembership()
    {
        await Save(Rule(10, 20)); await Reconcile();
        var rule = (await _rules.ListAsync(1)).Single(); rule.RoleIds = new ulong[] { 21 }; await Save(rule, false);
        await Reconcile(); Assert.That(_gateway.Removed, Is.EqualTo(new ulong[] { 20 }));
        rule.Enabled = false; await Save(rule, false); await Reconcile();
        Assert.That(_gateway.Removed, Is.EqualTo(new ulong[] { 20, 21 }));
        rule.Enabled = true; await Save(rule, false); await Reconcile();
        _gateway.Member = _gateway.Member! with { ChannelId = null }; await Reconcile();
        Assert.That(_gateway.Removed.Last(), Is.EqualTo(21));
    }

    [Test] public async Task RestartAndMissedEventsUseCurrentVoiceStateNotEventChannel()
    {
        await Save(Rule(10, 20)); await Reconcile();
        _reconciler = new VoiceRoleReconciler(_factory, _rules, _gateway,
            Options.Create(new VoiceLoggingOptions { Enabled = true, ConnectedRoleId = Connected }), NullLogger<VoiceRoleReconciler>.Instance);
        _gateway.Member = _gateway.Member! with { ChannelId = null };
        await _reconciler.ReconcileAsync(1, Array.Empty<ulong>(), CancellationToken.None);
        Assert.That(_gateway.Removed, Is.EqualTo(new ulong[] { 20 }));
    }

    [Test] public async Task MissingPermissionAndApiFailureAreRetryable()
    {
        await Save(Rule(10, 20)); _gateway.CanManage = false; await Reconcile(); Assert.That(_gateway.Added, Is.Empty);
        _gateway.CanManage = true; _gateway.Fail = true; await Reconcile(); Assert.That(_gateway.Added, Is.Empty);
        _gateway.Fail = false; await Reconcile(); Assert.That(_gateway.Added, Is.EqualTo(new ulong[] { 20 }));
    }

    [Test] public async Task AmbiguousPendingGrantNeverAuthorizesRemoval()
    {
        await Save(Rule(10, 20));
        await using (var db = _factory.CreateDbContext())
        { db.Add(new VoiceRoleOwnership { GuildId = 1, UserId = 3, RoleId = 20 }); await db.SaveChangesAsync(); }
        _gateway.Member = _gateway.Member! with { ChannelId = null, Roles = new ulong[] { 20 } };
        await Reconcile(); Assert.That(_gateway.Removed, Is.Empty);
        await using var check = _factory.CreateDbContext(); Assert.That(await check.VoiceRoleOwnerships.CountAsync(), Is.Zero);
    }

    [Test] public async Task FailedRemovalRetainsOwnershipAndRetriesAfterPermissionsRecover()
    {
        await Save(Rule(10, 20)); await Reconcile();
        _gateway.Member = _gateway.Member! with { ChannelId = null }; _gateway.Fail = true;
        await Reconcile();
        await using (var db = _factory.CreateDbContext()) Assert.That(await db.VoiceRoleOwnerships.CountAsync(), Is.EqualTo(1));
        _gateway.Fail = false; await Reconcile();
        Assert.That(_gateway.Removed, Is.EqualTo(new ulong[] { 20 }));
    }

    [Test] public async Task DeletedRoleWithNoActualGrantDropsObsoleteOwnershipSafely()
    {
        await Save(Rule(10, 20)); await Reconcile();
        _gateway.Member = _gateway.Member! with { Roles = Array.Empty<ulong>() }; _gateway.CanManage = false;
        await Reconcile();
        await using var db = _factory.CreateDbContext(); Assert.That(await db.VoiceRoleOwnerships.CountAsync(), Is.Zero);
        Assert.That(_gateway.Removed, Is.Empty);
    }

    [Test] public async Task MalformedPersistedRuleStillCannotGrantConnectedRole()
    {
        await using (var db = _factory.CreateDbContext())
        { db.Add(Rule(10, Connected)); await db.SaveChangesAsync(); }
        await Reconcile(); Assert.That(_gateway.Added, Is.Empty);
    }

    [Test] public void MissingConnectedProtectionRejectsRoleConfiguration()
    {
        Assert.Throws<ArgumentException>(() => VoiceRulesService.Validate(Rule(10, 20), 0));
        Assert.DoesNotThrow(() => VoiceRulesService.Validate(Rule(), 0));
    }

    [Test] public async Task UnavailableMemberIsNotAssumedDisconnected()
    {
        await Save(Rule(10, 20)); await Reconcile(); _gateway.Member = null; await Reconcile();
        Assert.That(_gateway.Removed, Is.Empty);
    }

    private class Factory : IDbContextFactory<MrCleanDbContext>
    {
        private readonly DbContextOptions<MrCleanDbContext> _options = new DbContextOptionsBuilder<MrCleanDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public MrCleanDbContext CreateDbContext() => new(_options);
    }
    private class FakeGateway : IVoiceRoleGateway
    {
        public VoiceRoleMember? Member;
        public bool CanManage = true, Fail;
        public List<ulong> Added = new(), Removed = new();
        public Task<VoiceRoleMember?> MemberAsync(ulong guild, ulong user, CancellationToken token) => Task.FromResult(Member);
        public Task<bool> CanManageAsync(ulong guild, ulong user, ulong role, CancellationToken token) => Task.FromResult(CanManage);
        public Task AddAsync(ulong guild, ulong user, ulong role, CancellationToken token)
        {
            if (Fail) throw new InvalidOperationException("Discord permission/API error");
            Added.Add(role); Member = Member! with { Roles = Member!.Roles.Append(role).Distinct().ToArray() }; return Task.CompletedTask;
        }
        public Task RemoveAsync(ulong guild, ulong user, ulong role, CancellationToken token)
        { if (Fail) throw new InvalidOperationException("Removal denied"); Removed.Add(role); Member = Member! with { Roles = Member!.Roles.Where(x => x != role).ToArray() }; return Task.CompletedTask; }
    }
}
