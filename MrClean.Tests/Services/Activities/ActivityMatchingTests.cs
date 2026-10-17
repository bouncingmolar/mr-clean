using System;
using System.Linq;
using Discord;
using MrClean.Models;
using MrClean.Services.Activities;
using NUnit.Framework;
namespace MrClean.Tests.Services.Activities;
public class ActivityMatchingTests
{
    private static ActivityRule Rule(string name, ulong role, ActivityType type = ActivityType.Playing) => new() { Name = name, RoleId = role, Type = type };
    [Test] public void EvaluatesAllActivitiesAndFallbacksPerUnmatchedActivity()
    {
        var activities = new[] { new ReportedActivity("Codenames", ActivityType.Playing), new ReportedActivity("Untracked", ActivityType.Playing), new ReportedActivity("Spotify", ActivityType.Listening) };
        var fallback = new[] { new ActivityFallback { Type = ActivityType.Playing, RoleId = 30, Enabled = true }, new ActivityFallback { Type = ActivityType.Listening, RoleId = 31, Enabled = true } };
        Assert.That(ActivityMatching.Roles(activities, new[] { Rule("Codenames", 20) }, fallback), Is.EquivalentTo(new ulong[] { 20, 30, 31 }));
        Assert.That(ActivityMatching.Roles(activities.Take(1), new[] { Rule("Codenames", 20) }, fallback), Is.EquivalentTo(new ulong[] { 20 }));
    }
    [Test] public void MultipleNamesSameRoleAndExactNameTypeApplicationMatching()
    {
        var rules = new[] { Rule("Codenames", 20), Rule("Codenames Online", 20), Rule("YouTube", 30, ActivityType.Watching) };
        rules[0].ApplicationId = 99;
        Assert.That(ActivityMatching.Roles(new[] { new ReportedActivity("Codenames", ActivityType.Playing, 99), new ReportedActivity("Codenames Online", ActivityType.Playing) }, rules, Array.Empty<ActivityFallback>()), Is.EquivalentTo(new ulong[] { 20 }));
        Assert.That(ActivityMatching.Roles(new[] { new ReportedActivity("codenames", ActivityType.Playing, 99), new ReportedActivity("YouTube", ActivityType.Listening), new ReportedActivity("Codenames", ActivityType.Playing, 100) }, rules, Array.Empty<ActivityFallback>()), Is.Empty);
    }
    [Test] public void DisabledExplicitRuleUsesEnabledFallbackAndUnsupportedTypesDoNotContribute()
    {
        var rule = Rule("Codenames", 20); rule.Enabled = false;
        var fallback = new ActivityFallback { Type = ActivityType.Playing, RoleId = 30, Enabled = true };
        Assert.That(ActivityMatching.Roles(new[] { new ReportedActivity("Codenames", ActivityType.Playing) }, new[] { rule }, new[] { fallback }), Is.EquivalentTo(new ulong[] { 30 }));
        fallback.Enabled = false;
        Assert.That(ActivityMatching.Roles(new[] { new ReportedActivity("custom", ActivityType.CustomStatus) }, new[] { rule }, new[] { fallback }), Is.Empty);
    }
    [TestCase(ActivityType.Playing)] [TestCase(ActivityType.Listening)] [TestCase(ActivityType.Watching)] [TestCase(ActivityType.Streaming)]
    public void EveryRequestedTypeIsSupported(ActivityType type) => Assert.That(ActivityMatching.Supported(type), Is.True);
    [Test] public void CacheDoesNotTurnMissingPresenceIntoInactivityOrAcceptOlderRevision()
    {
        var cache = new PresenceSnapshotCache();
        cache.Observe(1, 2, new[] { new ReportedActivity("Codenames", ActivityType.Playing) }, 10);
        cache.Observe(1, 2, Array.Empty<ReportedActivity>(), 9);
        Assert.That(cache.Get(1, 2).Activities.Single().Name, Is.EqualTo("Codenames"));
        cache.Invalidate(1); Assert.That(cache.Get(1, 2).Known, Is.False);
        Assert.That(cache.Get(1, 99).Known, Is.False);
    }
    [Test] public void FirstFreshEmptyGatewayUpdateImmediatelyConfirmsInactivity()
    {
        var cache = new PresenceSnapshotCache();
        cache.ObserveGateway(1, 2, new[] { new ReportedActivity("Codenames", ActivityType.Playing) });
        cache.ObserveGateway(1, 2, Array.Empty<ReportedActivity>());
        Assert.That(cache.Get(1, 2).Known, Is.True);
        Assert.That(cache.Get(1, 2).Activities, Is.Empty);
        cache.Invalidate(1);
        Assert.That(cache.Get(1, 2).Known, Is.False);
        cache.SeedPositive(1, 2, Array.Empty<ReportedActivity>());
        Assert.That(cache.Get(1, 2).Known, Is.False);
        cache.ObserveGateway(1, 2, Array.Empty<ReportedActivity>());
        Assert.That(cache.Get(1, 2).Known, Is.True);
    }
    [TestCase(UserStatus.Online)] [TestCase(UserStatus.Idle)]
    [TestCase(UserStatus.DoNotDisturb)] [TestCase(UserStatus.Offline)] [TestCase(UserStatus.Invisible)]
    public void FreshEmptyObserverUpdateIsAuthoritativeRegardlessOfStatusOrActiveClients(UserStatus status)
    {
        var cache = new PresenceSnapshotCache();
        cache.ObserveGateway(1, 2, new[] { new ReportedActivity("Codenames", ActivityType.Playing) });
        PresenceObserver.CaptureUpdate(cache, 1, 2, new EmptyPresence(status));
        Assert.That(cache.Get(1, 2).Known, Is.True);
        Assert.That(cache.Get(1, 2).Activities, Is.Empty);
    }
    private sealed class EmptyPresence : IPresence
    {
        public EmptyPresence(UserStatus status) => Status = status;
        public UserStatus Status { get; }
        public System.Collections.Generic.IReadOnlyCollection<ClientType> ActiveClients => Array.Empty<ClientType>();
        public System.Collections.Generic.IReadOnlyCollection<IActivity> Activities => Array.Empty<IActivity>();
    }
    [Test] public void LateStartupCacheCannotResurrectAnActivityEndedByFreshUpdate()
    {
        var cache = new PresenceSnapshotCache();
        cache.ObserveGateway(1, 2, Array.Empty<ReportedActivity>());
        cache.SeedPositive(1, 2, new[] { new ReportedActivity("Codenames", ActivityType.Playing) });
        Assert.That(cache.Get(1, 2).Known, Is.True);
        Assert.That(cache.Get(1, 2).Activities, Is.Empty);
    }
    [Test] public void StartupPositiveCacheDoesNotClaimCompleteness()
    {
        var cache = new PresenceSnapshotCache(); cache.SeedPositive(1, 2, new[] { new ReportedActivity("game", ActivityType.Playing) });
        Assert.That(cache.Get(1, 2).Known, Is.False); Assert.That(cache.Get(1, 2).Activities.Count, Is.EqualTo(1));
        cache.Forget(1, 2); Assert.That(cache.Users(1), Is.Empty);
    }
}
