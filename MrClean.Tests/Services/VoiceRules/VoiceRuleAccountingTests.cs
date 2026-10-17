using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MrClean.Models;
using MrClean.Services.VoiceLogging;
using NUnit.Framework;

namespace MrClean.Tests.Services.VoiceRules;

public class VoiceRuleAccountingTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-17T00:00:00Z");
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(120);
    [Test] public void OptionalLogsTimingAndInterruptionSettingsAreIndependent()
    {
        var state = new VoiceActivityTracker();
        var rule = new VoiceChannelRule { LogJoin = false, LogDeparture = true, TrackDuration = false, TrackInterruptions = false };
        state.Transition(1, 10, "Listening to Music", Start, Window, 99, rule: rule);
        state.Transition(1, null, null, Start.AddSeconds(60), Window, 99);
        Assert.That(state.Pending.Count, Is.EqualTo(1));
        Assert.That(state.Pending.Single().Text, Does.Contain("Listening to Music").And.Not.Contain("Connected for").And.Not.Contain("interruptions"));
        Assert.That(state.Sessions.Single().ConnectedSeconds, Is.Zero);
        state.Transition(1, 10, "Music", Start.AddSeconds(70), Window, 99, rule: rule);
        Assert.That(state.Sessions.Single().Started, Is.EqualTo(Start.AddSeconds(70)));
        Assert.That(state.Sessions.Single().Interruptions, Is.Zero);
    }

    [Test] public void MissedDepartureUsesLastCheckpointAndNewRuleOccupantStartsAtObservation()
    {
        var state = new VoiceActivityTracker();
        state.Transition(1, 10, "Music", Start, Window, 99);
        state.Checkpoint(Start.AddSeconds(30), Window);
        var rules = new Dictionary<ulong, VoiceChannelRule> { [10] = new() { Enabled = true }, [11] = new() { ActivityName = "Codenames", DestinationId = 200 } };
        VoiceRuleObservation.Reconcile(state, new[] { new VoiceOccupant(1, 11, "game") }, rules, Start.AddSeconds(60), Window, 99);
        Assert.That(state.Pending[1].Text, Does.Contain("30 seconds").And.Contain("observation gap"));
        Assert.That(state.Pending[2].Destination, Is.EqualTo(200));
        Assert.That(state.Sessions.Single().ConnectedSince, Is.EqualTo(Start.AddSeconds(60)));
    }

    [Test] public void DisabledRuleStopsTrackingAndLeavesNoMisleadingDepartureLog()
    {
        var state = new VoiceActivityTracker();
        state.Transition(1, 10, "Music", Start, Window, 99);
        VoiceRuleObservation.Reconcile(state, new[] { new VoiceOccupant(1, 10, "music") },
            new Dictionary<ulong, VoiceChannelRule>(), Start.AddSeconds(60), Window, 99);
        Assert.That(state.Sessions.Single().ConnectedSince, Is.Null);
        Assert.That(state.Pending.Count, Is.EqualTo(1));
    }

    [Test] public void LegacyActiveSessionSurvivesSerializationAndContinuesWithoutResettingTimeOrDestination()
    {
        var state = new VoiceActivityTracker();
        state.Transition(1, 10, "Voice", Start, Window, 99);
        state.Checkpoint(Start.AddSeconds(30), Window);
        state = JsonSerializer.Deserialize<VoiceActivityTracker>(JsonSerializer.Serialize(state))!;
        state.Transition(1, null, null, Start.AddSeconds(40), Window, 99, uncertain: true);
        var rule = new VoiceChannelRule { ActivityName = "Voice", DynamicName = true, DestinationId = 200 };
        state.Transition(1, 10, "Among Us", Start.AddSeconds(40), Window, 200, rule: rule);
        Assert.That(state.Sessions.Single().Started, Is.EqualTo(Start));
        Assert.That(state.Sessions.Single().ConnectedSeconds, Is.EqualTo(30));
        Assert.That(state.Sessions.Single().Destination, Is.EqualTo(99));
    }

    [Test] public void ActiveTimingPolicyIsSnapshotAndDoesNotMutateRuleCache()
    {
        var rule = new VoiceChannelRule { TrackDuration = true };
        var state = new VoiceActivityTracker(); state.Transition(1, 10, "Music", Start, Window, 99, rule: rule);
        rule.TrackDuration = false;
        state.Transition(1, null, null, Start.AddSeconds(60), Window, 99);
        Assert.That(state.Sessions.Single().ConnectedSeconds, Is.EqualTo(60));
    }
}
