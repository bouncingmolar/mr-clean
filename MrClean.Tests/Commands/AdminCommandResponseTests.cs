using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Logging.Abstractions;
using MrClean.Commands.Administration;
using MrClean.Commands.Activities;
using MrClean.Commands.Voice;
using MrClean.Models;
using NUnit.Framework;

namespace MrClean.Tests.Commands;

public class AdminCommandResponseTests
{
    private static Task<AdminCommandResponse?> Start(Fake interaction, string root = "activity", string action = "track", bool authorized = true) =>
        AdminCommandResponse.StartAsync(interaction, root, action, authorized, NullLogger.Instance);

    [Test] public async Task EveryConfigurationAndListCommandDefersAndCompletesPublicly()
    {
        foreach (var (root, commands) in new[] { ("activity", ActivityCommand.Build().Options.Value), ("voice", VoiceCommand.Build().Options.Value) })
        foreach (var command in commands.Where(x => x.Name != "inspect"))
        {
            var transport = new Fake(); var response = await Start(transport, root, command.Name);
            await response!.ExecuteAsync(() => Task.FromResult("Committed configuration."));
            Assert.That(transport.Calls.Select(x => (x.Kind, x.Private)), Is.EqualTo(new[] { ("defer", false), ("original", false) }), root + " " + command.Name);
        }
    }

    [Test] public async Task InspectionAndEveryInspectionChunkStayPrivate()
    {
        var transport = new Fake(); var response = await Start(transport, action: "inspect");
        await response!.ExecuteAsync(() => Task.FromResult("Private member activity " + new string('x', 4000)));
        Assert.That(transport.Calls.All(x => x.Private), Is.True);
        Assert.That(transport.Calls.Count(x => x.Kind == "original"), Is.EqualTo(1));
        Assert.That(transport.Calls.Where(x => x.Kind != "defer").All(x => x.Text.Length <= 1800), Is.True);
    }

    [Test] public async Task LongPublicListsCompleteOriginalThenSendPublicContinuations()
    {
        var transport = new Fake(); var response = await Start(transport, "voice", "list");
        await response!.ExecuteAsync(() => Task.FromResult(new string('x', 4100)));
        Assert.That(transport.Calls.Select(x => x.Kind), Is.EqualTo(new[] { "defer", "original", "followup", "followup" }));
        Assert.That(transport.Calls.Any(x => x.Private), Is.False);
    }

    [Test] public async Task UnauthorizedRequestsAreRejectedPrivatelyBeforeDeferral()
    {
        var transport = new Fake(); var response = await Start(transport, authorized: false);
        Assert.That(response, Is.Null);
        Assert.That(transport.Calls.Single().Kind, Is.EqualTo("reject"));
        Assert.That(transport.Calls.Single().Private, Is.True);
    }

    [Test] public async Task NoPublicSuccessUntilOperationActuallyCompletes()
    {
        var transport = new Fake(); var response = await Start(transport);
        var committed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = response!.ExecuteAsync(() => committed.Task);
        Assert.That(transport.Calls.Select(x => x.Kind), Is.EqualTo(new[] { "defer" }));
        committed.SetResult("Added rule after commit."); await pending;
        Assert.That(transport.Calls.Last().Text, Is.EqualTo("Added rule after commit."));
    }

    [Test] public async Task ValidationErrorsResolvePublicDeferBeforePrivateDetails()
    {
        var transport = new Fake(); var response = await Start(transport, action: "confirm-empty");
        await response!.ExecuteAsync(() => throw new ArgumentException("A supported activity is still cached."));
        Assert.That(transport.Calls.Select(x => (x.Kind, x.Private)),
            Is.EqualTo(new[] { ("defer", false), ("original", false), ("followup", true) }));
        Assert.That(transport.Calls.Single(x => x.Kind == "original").Text, Does.Not.Contain("cached").And.Not.Contain("success"));
        Assert.That(transport.Calls.Last().Text, Does.Contain("cached"));
    }

    [Test] public async Task UnexpectedFailureDoesNotExposeInternalDetailsOrClaimRollback()
    {
        var transport = new Fake(); var response = await Start(transport); var persisted = false;
        await response!.ExecuteAsync(() => { persisted = true; throw new InvalidOperationException("Password=SECRET; database stack trace"); });
        Assert.That(persisted, Is.True);
        Assert.That(string.Join("\n", transport.Calls.Select(x => x.Text)), Does.Not.Contain("SECRET").And.Not.Contain("stack trace"));
        Assert.That(transport.Calls.Single(x => x.Kind == "original").Text, Does.Contain("may already have committed").And.Not.Contain("saved"));
    }

    [Test] public async Task InspectionErrorsNeverProducePublicMessages()
    {
        var transport = new Fake(); var response = await Start(transport, action: "inspect");
        await response!.ExecuteAsync(() => throw new ArgumentException("Select a human member."));
        Assert.That(transport.Calls.All(x => x.Private), Is.True);
    }

    [Test] public async Task DeliveryFailureDoesNotRetryCommittedOperationOrReportItFailed()
    {
        var transport = new Fake { FailDelivery = true }; var response = await Start(transport); var commits = 0;
        Assert.ThrowsAsync<InvalidOperationException>(() => response!.ExecuteAsync(() => { commits++; return Task.FromResult("Added rule."); }));
        Assert.That(commits, Is.EqualTo(1));
        Assert.That(transport.Calls.Any(x => x.Kind == "followup"), Is.False);
    }

    [Test] public void ConfirmationsIdentifyRuleTypeRoleApplicationAndStatus()
    {
        var rule = new ActivityRule { Id = 7, Name = "Gartic", Type = ActivityType.Playing, RoleId = 20, Enabled = false, ApplicationId = 99 };
        var text = AdminSettingsText.Activity(rule);
        Assert.That(text, Does.Contain("Rule 7").And.Contain("Playing").And.Contain("Gartic").And.Contain("<@&20>").And.Contain("disabled").And.Contain("99"));
        Assert.That(AdminSettingsText.Fallback(ActivityType.Listening, 30, true), Does.Contain("Listening").And.Contain("<@&30>").And.Contain("enabled"));
    }

    [Test] public void VoiceSettingsIncludeChannelDestinationTogglesWordingAndRoles()
    {
        var rule = new VoiceChannelRule { ChannelId = 10, DestinationId = 11, ActivityName = "Voice", Wording = "Playing {activity}", DynamicName = true, LogJoin = false, RoleIds = new ulong[] { 20 } };
        var text = VoiceCommand.Describe(rule);
        Assert.That(text, Does.Contain("<#10>").And.Contain("<#11>").And.Contain("dynamic current channel name").And.Contain("Playing {activity}"));
        Assert.That(text, Does.Contain("join log disabled").And.Contain("departure log enabled").And.Contain("duration enabled").And.Contain("interruptions enabled").And.Contain("<@&20>"));
    }

    [Test] public void ManagementStateDistinguishesEnabledFromCapturePaused()
    {
        Assert.That(AdminSettingsText.Management(false, true), Does.Contain("Global presence-role management: disabled").And.Contain("capture: enabled"));
        Assert.That(AdminSettingsText.Management(true, false), Does.Contain("management: enabled").And.Contain("assignments are paused"));
    }

    [Test] public void AdoptionSummaryReportsRoleAndCountWithoutMemberPresenceInformation()
    {
        var text = AdminSettingsText.Adoption(20, 4);
        Assert.That(text, Does.Contain("4 existing grant").And.Contain("<@&20>").And.Contain("Ownership recorded"));
        Assert.That(System.Text.RegularExpressions.Regex.IsMatch(text, @"<@!?\d+>"), Is.False);
        Assert.That(text, Does.Not.Contain("Codenames").And.Not.Contain("application"));
    }

    [Test] public void ZeroEligibleAdoptionsDoesNotClaimOwnershipWasAcquired()
    {
        Assert.That(AdminSettingsText.Adoption(20, 0), Does.Contain("No eligible existing grants").And.Contain("no ownership was acquired").And.Not.Contain("Ownership recorded"));
    }

    [Test] public async Task DuplicateRuleFailureKeepsSavedConfigurationAndNeverAnnouncesAdded()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<MrClean.Data.MrCleanDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var service = new MrClean.Services.Activities.ActivityRulesService(new Factory(options));
        await service.SaveAsync(new ActivityRule { GuildId = 1, Name = "Codenames", RoleId = 20 }, true, 999);
        var transport = new Fake(); var response = await Start(transport);
        await response!.ExecuteAsync(async () =>
        {
            var duplicate = new ActivityRule { GuildId = 1, Name = "Codenames", RoleId = 21 };
            await service.SaveAsync(duplicate, true, 999);
            return "Added " + AdminSettingsText.Activity(duplicate);
        });
        Assert.That((await service.GetAsync(1)).Rules.Single().RoleId, Is.EqualTo(20UL));
        Assert.That(transport.Calls.Where(x => !x.Private).Any(x => x.Text.Contains("Added")), Is.False);
        Assert.That(transport.Calls.Last().Private, Is.True);
    }

    private sealed class Factory : Microsoft.EntityFrameworkCore.IDbContextFactory<MrClean.Data.MrCleanDbContext>
    {
        private readonly Microsoft.EntityFrameworkCore.DbContextOptions<MrClean.Data.MrCleanDbContext> _options;
        public Factory(Microsoft.EntityFrameworkCore.DbContextOptions<MrClean.Data.MrCleanDbContext> options) => _options = options;
        public MrClean.Data.MrCleanDbContext CreateDbContext() => new(_options);
    }

    [Test] public void UntrustedSettingsCannotBreakOutOfCodeFormatting()
    {
        var text = AdminSettingsText.Quote("game`\n@everyone");
        Assert.That(text.Count(x => x == '`'), Is.EqualTo(2));
        Assert.That(text, Does.Not.Contain("\n").And.Contain("\\u0060").And.Contain("\\n"));
    }

    private sealed class Fake : IAdminCommandInteraction
    {
        public List<(string Kind, bool Private, string Text)> Calls = new();
        private bool _private; public bool FailDelivery;
        public Task RejectAsync(string text) { Calls.Add(("reject", true, text)); return Task.CompletedTask; }
        public Task DeferAsync(bool ephemeral) { _private = ephemeral; Calls.Add(("defer", ephemeral, "")); return Task.CompletedTask; }
        public Task CompleteAsync(string text)
        { if (FailDelivery) throw new InvalidOperationException("Discord unavailable"); Calls.Add(("original", _private, text)); return Task.CompletedTask; }
        public Task FollowupAsync(string text, bool ephemeral) { Calls.Add(("followup", ephemeral, text)); return Task.CompletedTask; }
    }
}
