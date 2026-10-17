using System;
using System.Linq;
using System.Text.Json;
using MrClean.Configuration;
using MrClean.Services.VoiceLogging;
using NUnit.Framework;

namespace MrClean.Tests.Services.VoiceLogging;

public class VoiceActivityTrackerTests
{
    private VoiceActivityTracker _tracker = null!;
    private readonly DateTimeOffset _start = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
    private void Move(int seconds, ulong? channel = null, string? activity = null, bool uncertain = false) =>
        _tracker.Transition(1, channel, activity, _start.AddSeconds(seconds), TimeSpan.FromSeconds(120), 99, uncertain);

    [SetUp] public void SetUp() => _tracker = new VoiceActivityTracker();

    [Test] public void JoinAndDisconnectAreSeparateImmediateLogs()
    {
        Move(0, 10, "Among Us");
        var original = _tracker.Pending.Single();
        Move(65);
        Assert.That(_tracker.Pending.Count, Is.EqualTo(2));
        Assert.That(_tracker.Pending[0], Is.EqualTo(original));
        Assert.That(_tracker.Pending[1].Text, Does.Contain("Among Us").And.Contain("0h 1m 5s"));
    }

    [Test] public void ReconnectPreservesActivityAndExcludesGap()
    {
        Move(0, 10, "Among Us"); Move(60); Move(90, 10, "Renamed"); Move(150);
        var session = _tracker.Sessions.Single();
        Assert.That(session.Started, Is.EqualTo(_start));
        Assert.That(session.Activity, Is.EqualTo("Among Us"));
        Assert.That(session.ConnectedSeconds, Is.EqualTo(120));
        Assert.That(session.Interruptions, Is.EqualTo(1));
        Assert.That(_tracker.Pending[2].Text, Does.Contain("30s excluded"));
    }

    [TestCase(180, true)] [TestCase(181, false)]
    public void ReconnectWindowIncludesBoundary(int seconds, bool same)
    {
        Move(0, 10, "Old"); Move(60); Move(seconds, 10, "New");
        Assert.That(_tracker.Sessions.Single().Started == _start, Is.EqualTo(same));
    }

    [Test] public void SwitchEndsOldActivityBeforeJoiningNewActivity()
    {
        Move(0, 10, "A"); Move(20, 11, "B");
        Assert.That(_tracker.Pending[1].Text, Does.Contain("left A"));
        Assert.That(_tracker.Pending[2].Text, Does.Contain("joined B"));
        Assert.That(_tracker.Sessions.Single().ConnectedSeconds, Is.Zero);
    }

    [Test] public void DuplicateVoiceStateDoesNotCreateLogOrChangeCapturedName()
    {
        Move(0, 10, "A"); Move(20, 10, "B");
        Assert.That(_tracker.Pending.Count, Is.EqualTo(1));
        Assert.That(_tracker.Sessions.Single().Activity, Is.EqualTo("A"));
    }

    [Test] public void RestartPreservesQueueAndCountsOnlyConfirmedTime()
    {
        Move(0, 10, "A"); _tracker.Checkpoint(_start.AddSeconds(30), TimeSpan.FromSeconds(120));
        _tracker = JsonSerializer.Deserialize<VoiceActivityTracker>(JsonSerializer.Serialize(_tracker))!;
        Move(60, uncertain: true); Move(60, 10, "B"); Move(90);
        Assert.That(_tracker.Sessions.Single().ConnectedSeconds, Is.EqualTo(60));
        Assert.That(_tracker.Pending[1].Text, Does.Contain("actual departure unknown"));
        Assert.That(_tracker.Pending[2].Text, Does.Contain("same session"));
    }

    [Test] public void StaleDisconnectedSessionsExpireButConnectedSessionsStay()
    {
        Move(0, 10, "A"); Move(10);
        _tracker.Checkpoint(_start.AddSeconds(131), TimeSpan.FromSeconds(120));
        Assert.That(_tracker.Sessions, Is.Empty);
        Move(140, 10, "B");
        _tracker.Checkpoint(_start.AddDays(1), TimeSpan.FromSeconds(120));
        Assert.That(_tracker.Sessions.Count, Is.EqualTo(1));
    }

    [Test] public void LongOutageStartsNewSessionAndDoesNotCountOfflineTime()
    {
        Move(0, 10, "A");
        _tracker.Checkpoint(_start.AddSeconds(30), TimeSpan.FromSeconds(120));
        Move(300, uncertain: true); Move(300, 10, "B");
        Assert.That(_tracker.Pending[1].Text, Does.Contain("0h 0m 30s"));
        Assert.That(_tracker.Sessions.Single().Activity, Is.EqualTo("B"));
        Assert.That(_tracker.Sessions.Single().Started, Is.EqualTo(_start.AddSeconds(300)));
    }

    [Test] public void QueueKeepsDestinationAndOrderAcrossSerialization()
    {
        Move(0, 10, "A"); Move(10);
        var restored = JsonSerializer.Deserialize<VoiceActivityTracker>(JsonSerializer.Serialize(_tracker))!;
        Assert.That(restored.Pending, Is.EqualTo(_tracker.Pending));
        Assert.That(restored.Pending.All(x => x.Destination == 99), Is.True);
    }

    [Test] public void ActivityCannotInsertNewlinesOrMentionsIntoLog()
    {
        Move(0, 10, "Game\n@everyone <@123>");
        Assert.That(_tracker.Pending.Single().Text, Does.Not.Contain("\n").And.Not.Contain("@everyone").And.Not.Contain("<@123>"));
    }

    [Test] public void GeneralChannelRenameAppliesToEveryDepartureWithoutEditingJoins()
    {
        Move(0, 10, "Voice");
        _tracker.Transition(2, 10, "Voice", _start, TimeSpan.FromSeconds(120), 99);
        var joins = _tracker.Pending.ToArray();
        foreach (ulong user in new ulong[] { 1, 2 })
            _tracker.Transition(user, null, null, _start.AddSeconds(60), TimeSpan.FromSeconds(120), 99,
                activityAtEnd: id => id == 10 ? "Among Us" : null);
        Assert.That(_tracker.Pending.Take(2), Is.EqualTo(joins));
        Assert.That(_tracker.Pending.Skip(2).All(x => x.Text.Contains("left Among Us")), Is.True);
    }

    [Test] public void SwitchResolvesDepartingChannelAndPreservesFixedDestinationLabel()
    {
        Move(0, 10, "Voice");
        _tracker.Transition(1, 11, "Codenames", _start.AddSeconds(60), TimeSpan.FromSeconds(120), 99,
            activityAtEnd: id => id == 10 ? "Among Us" : null);
        _tracker.Transition(1, null, null, _start.AddSeconds(90), TimeSpan.FromSeconds(120), 99,
            activityAtEnd: id => id == 10 ? "Renamed again" : null);
        Assert.That(_tracker.Pending[1].Text, Does.Contain("left Among Us"));
        Assert.That(_tracker.Pending[2].Text, Does.Contain("joined Codenames"));
        Assert.That(_tracker.Pending[3].Text, Does.Contain("left Codenames"));
    }

    [Test] public void RestartRecoveryUsesCurrentGeneralChannelName()
    {
        Move(0, 10, "Voice");
        _tracker = JsonSerializer.Deserialize<VoiceActivityTracker>(JsonSerializer.Serialize(_tracker))!;
        _tracker.Transition(1, null, null, _start.AddSeconds(60), TimeSpan.FromSeconds(120), 99,
            uncertain: true, activityAtEnd: _ => "Lethal Company");
        Assert.That(_tracker.Pending[1].Text, Does.Contain("left Lethal Company"));
    }

    [Test] public void MissingChannelFallsBackToStoredActivity()
    {
        Move(0, 10, "Voice");
        _tracker.Transition(1, null, null, _start.AddSeconds(60), TimeSpan.FromSeconds(120), 99,
            activityAtEnd: _ => null);
        Assert.That(_tracker.Pending[1].Text, Does.Contain("left Voice"));
    }

    [Test] public void SessionDestinationSurvivesReconnectConfigurationChangeAndRestart()
    {
        _tracker.Transition(1, 10, "Music", _start, TimeSpan.FromSeconds(120), 100);
        _tracker = JsonSerializer.Deserialize<VoiceActivityTracker>(JsonSerializer.Serialize(_tracker))!;
        _tracker.Transition(1, null, null, _start.AddSeconds(30), TimeSpan.FromSeconds(120), 200);
        _tracker.Transition(1, 10, "Music", _start.AddSeconds(60), TimeSpan.FromSeconds(120), 200);
        Assert.That(_tracker.Pending.All(x => x.Destination == 100), Is.True);
    }

    [Test] public void RoutingUsesSourceIdsAndGeneralRenameDoesNotRouteToGames()
    {
        var options = new VoiceLoggingOptions { GeneralChannelId = 10, LogChannelId = 806802061693550652 };
        options.Channels.Add(11, "Music"); options.Channels.Add(12, "Codenames");
        options.ChannelLogDestinations.Add(11, 943558876706533477);
        options.ChannelLogDestinations.Add(12, 1556935771049689119);
        Assert.That(options.Activity(10, "Among Us"), Is.EqualTo("Among Us"));
        Assert.That(options.Destination(10), Is.EqualTo(806802061693550652UL));
        Assert.That(options.Destination(11), Is.EqualTo(943558876706533477UL));
        Assert.That(options.Destination(12), Is.EqualTo(1556935771049689119UL));
        Assert.That(options.Destination(13), Is.EqualTo(options.LogChannelId));
    }

    [Test] public void ChannelConfigurationUsesIdsAndGeneralChannelName()
    {
        var options = new VoiceLoggingOptions { GeneralChannelId = 10 };
        options.Channels.Add(11, "Codenames");
        Assert.That(options.Activity(10, "Lethal Company"), Is.EqualTo("Lethal Company"));
        Assert.That(options.Activity(11, "Renamed"), Is.EqualTo("Codenames"));
        Assert.That(options.Activity(12, "Codenames"), Is.Null);
        Assert.That(options.ReconnectWindowSeconds, Is.EqualTo(120));
    }
}
