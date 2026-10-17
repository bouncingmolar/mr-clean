using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MrClean.Models;
using MrClean.Services.VoiceLogging;
using NUnit.Framework;

namespace MrClean.Tests.Services.VoiceLogging;

public class VoicePipelineTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Test]
    public async Task SlowPersistenceDoesNotDelayCaptureOrChangeReconnectTiming()
    {
        var pump = new VoiceEventPump();
        var state = new VoiceActivityTracker();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var blocked = Signal(); var release = Signal(); var finished = Signal();
        var commits = 0;
        var processing = pump.RunAsync(state, async () =>
        {
            if (++commits == 1) { blocked.SetResult(true); await release.Task; }
            if (commits == 4) finished.SetResult(true);
        }, stop.Token);
        void Capture(int seconds, ulong? channel) => pump.Capture(s => s.Transition(1, channel,
            channel == null ? null : "Voice", Start.AddSeconds(seconds), TimeSpan.FromSeconds(120), 99));
        Capture(0, 10);
        await blocked.Task.WaitAsync(stop.Token);
        Capture(60, null); Capture(180, 10); Capture(240, null);
        Assert.That(commits, Is.EqualTo(1), "Events can be captured while the writer is blocked.");
        release.SetResult(true);
        await finished.Task.WaitAsync(stop.Token);
        Assert.That(state.Sessions.Single().ConnectedSeconds, Is.EqualTo(120));
        Assert.That(state.Sessions.Single().Interruptions, Is.EqualTo(1));
        Assert.That(state.Pending[1].Text, Does.StartWith($"<t:{Start.AddSeconds(60).ToUnixTimeSeconds()}:T>"));
        Assert.That(state.Pending[2].Text, Does.Contain("same session").And.Contain("2m 0s excluded"));
        stop.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await processing);
    }

    [Test]
    public async Task DelayedDeliveryDoesNotBlockRapidSwitchesAndReconnects()
    {
        var pump = new VoiceEventPump(); var state = new VoiceActivityTracker();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sending = Signal(); var release = Signal(); var captured = Signal();
        var delivered = new List<string>();
        var first = new VoiceLogMessage { Text = "earlier durable message" };
        var delivery = VoiceLogDelivery.DeliverAsync(first, () => Task.FromResult(false), () => Task.CompletedTask,
            async () => { sending.SetResult(true); await release.Task; delivered.Add(first.Text); }, () => Task.CompletedTask);
        await sending.Task.WaitAsync(stop.Token);
        var committed = 0;
        var processing = pump.RunAsync(state, () =>
        {
            if (++committed == 4) captured.SetResult(true);
            return Task.CompletedTask;
        }, stop.Token);
        void Capture(int seconds, ulong? channel, string? activity, ulong destination) => pump.Capture(s =>
            s.Transition(1, channel, activity, Start.AddSeconds(seconds), TimeSpan.FromSeconds(120), destination));
        Capture(0, 10, "Music", 100); Capture(1, 11, "Codenames", 200);
        Capture(2, null, null, 200); Capture(3, 11, "Codenames", 200);
        await captured.Task.WaitAsync(stop.Token);
        Assert.That(delivery.IsCompleted, Is.False);
        Assert.That(state.Pending.Select(x => x.Destination), Is.EqualTo(new ulong[] { 100, 100, 200, 200, 200 }));
        Assert.That(state.Pending[1].Text, Does.Contain("left Music"));
        Assert.That(state.Pending[2].Text, Does.Contain("joined Codenames"));
        Assert.That(state.Sessions.Single().ConnectedSeconds, Is.EqualTo(1));
        release.SetResult(true); await delivery;
        stop.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await processing);
    }

    [Test]
    public async Task CompletingPumpDrainsCapturedObservationsForGracefulShutdown()
    {
        var pump = new VoiceEventPump(); var state = new VoiceActivityTracker();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        pump.Capture(s => s.Transition(1, 10, "Voice", Start, TimeSpan.FromSeconds(120), 99));
        pump.Capture(s => s.Transition(1, null, null, Start.AddSeconds(60), TimeSpan.FromSeconds(120), 99));
        pump.Complete();
        var commits = 0;
        await pump.RunAsync(state, () => { commits++; return Task.CompletedTask; }, stop.Token);
        Assert.That(commits, Is.EqualTo(2));
        Assert.That(state.Sessions.Single().ConnectedSeconds, Is.EqualTo(60));
    }

    [Test]
    public void CommitAssignsStableOrderedIdsAndRecoversSequenceAfterRestart()
    {
        var state = new VoiceActivityTracker();
        state.Pending.Add(new VoiceLog(100, "identical"));
        state.Pending.Add(new VoiceLog(100, "identical"));
        var commit = VoiceStateCommit.Prepare(state);
        Assert.That(commit.Logs.Select(x => x.Id), Is.EqualTo(new long[] { 1, 2 }));
        Assert.That(commit.Logs[0].Text, Is.Not.EqualTo(commit.Logs[1].Text));
        var restored = System.Text.Json.JsonSerializer.Deserialize<VoiceActivityTracker>(commit.Json)!;
        Assert.That(restored.Pending, Is.Empty);
        restored.Pending.Add(new VoiceLog(200, "next"));
        Assert.That(VoiceStateCommit.Prepare(restored).Logs.Single().Id, Is.EqualTo(3));
    }

    [Test]
    public async Task UncertainAcknowledgementReconcilesBeforeResending()
    {
        var log = new VoiceLogMessage(); var sends = 0; var exists = false; var acknowledgements = 0;
        Task Send() { sends++; exists = true; return Task.CompletedTask; }
        Task Ack() { if (++acknowledgements == 1) throw new InvalidOperationException("Database unavailable"); return Task.CompletedTask; }
        Assert.ThrowsAsync<InvalidOperationException>(() => VoiceLogDelivery.DeliverAsync(log,
            () => Task.FromResult(exists), () => Task.CompletedTask, Send, Ack));
        await VoiceLogDelivery.DeliverAsync(log, () => Task.FromResult(exists), () => Task.CompletedTask, Send, Ack);
        Assert.That(sends, Is.EqualTo(1));
        Assert.That(acknowledgements, Is.EqualTo(2));
    }

    [Test]
    public void FailedIntentWriteNeverSendsMessage()
    {
        var sends = 0;
        Assert.ThrowsAsync<InvalidOperationException>(() => VoiceLogDelivery.DeliverAsync(new VoiceLogMessage(),
            () => Task.FromResult(false), () => throw new InvalidOperationException("Cannot persist intent"),
            () => { sends++; return Task.CompletedTask; }, () => Task.CompletedTask));
        Assert.That(sends, Is.Zero);
    }

    [Test]
    public async Task RetryAfterUnsentIntentChecksHistoryThenSends()
    {
        var steps = new List<string>();
        await VoiceLogDelivery.DeliverAsync(new VoiceLogMessage { AttemptedAt = Start },
            () => { steps.Add("history"); return Task.FromResult(false); },
            () => { steps.Add("intent"); return Task.CompletedTask; },
            () => { steps.Add("send"); return Task.CompletedTask; },
            () => { steps.Add("ack"); return Task.CompletedTask; });
        Assert.That(steps, Is.EqualTo(new[] { "history", "intent", "send", "ack" }));
    }
}
