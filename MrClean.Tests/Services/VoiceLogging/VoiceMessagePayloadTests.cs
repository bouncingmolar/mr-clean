using System.Linq;
using MrClean.Services.VoiceLogging;
using NUnit.Framework;

namespace MrClean.Tests.Services.VoiceLogging;

public class VoiceMessagePayloadTests
{
    [Test]
    public void DepartureHasOnlyDescriptionNoContentFieldsFooterOrTimestamp()
    {
        var state = new VoiceActivityTracker();
        state.Pending.Add(new VoiceLog(99, "<@1> left <#10> (Music)\nConnected for 12 minutes.\n0 brief interruptions.", true));
        var payload = VoiceMessagePayload.Parse(VoiceStateCommit.Prepare(state).Logs.Single().Text);
        var embed = payload.BuildEmbed()!;
        Assert.That(payload.Content, Is.Null);
        Assert.That(embed.Description, Is.EqualTo("<@1> left <#10> (Music)\nConnected for 12 minutes.\n0 brief interruptions."));
        Assert.That(embed.Fields, Is.Empty);
        Assert.That(embed.Footer, Is.Null);
        Assert.That(embed.Timestamp, Is.Null);
        Assert.That(embed.Author, Is.Null);
        Assert.That(embed.Title, Is.Null);
    }

    [Test]
    public void JoinIsExactlyOneLineWithRealMentionsAndNoEmbed()
    {
        var state = new VoiceActivityTracker();
        state.Pending.Add(new VoiceLog(99, "🔊 <@1> joined <#10>"));
        var payload = VoiceMessagePayload.Parse(VoiceStateCommit.Prepare(state).Logs.Single().Text);
        Assert.That(payload.Content, Is.EqualTo("🔊 <@1> joined <#10>"));
        Assert.That(payload.BuildEmbed(), Is.Null);
    }

    [Test]
    public void RetryBaselineExcludesPriorIdenticalMessagesAndSurvivesRestart()
    {
        var payload = VoiceMessagePayload.Parse(new VoiceMessagePayload { Content = "🔊 <@1> joined <#10>", HistoryBaseline = 100 }.Serialize());
        Assert.That(payload.Matches(100, payload.Content, new string[0]), Is.False);
        Assert.That(payload.Matches(101, payload.Content, new string[0]), Is.True);
        Assert.That(payload.Matches(101, "different", new string[0]), Is.False);
        var departure = new VoiceMessagePayload { Description = "left", HistoryBaseline = 100 };
        Assert.That(departure.Matches(101, "", new[] { "left" }), Is.True);
        Assert.That(departure.Matches(101, "left", new string[0]), Is.False);
    }

    [Test]
    public void LegacyQueuedMessagesRemainReadableWithoutSchemaChanges()
    {
        var payload = VoiceMessagePayload.Parse("original queued message [vc:12]");
        Assert.That(payload.Content, Is.EqualTo("original queued message [vc:12]"));
        Assert.That(payload.BuildEmbed(), Is.Null);
    }

    [Test]
    public void PreviouslyPersistedSessionQueueDefaultsToLegacyPlainText()
    {
        var state = System.Text.Json.JsonSerializer.Deserialize<VoiceActivityTracker>(
            "{\"Pending\":[{\"Destination\":99,\"Text\":\"legacy join\"}]}")!;
        var payload = VoiceMessagePayload.Parse(VoiceStateCommit.Prepare(state).Logs.Single().Text);
        Assert.That(payload.Content, Is.EqualTo("legacy join"));
        Assert.That(payload.BuildEmbed(), Is.Null);
    }

    [TestCase(0, "0 seconds")]
    [TestCase(1, "1 second")]
    [TestCase(720, "12 minutes")]
    [TestCase(3661, "1 hour, 1 minute, 1 second")]
    public void DurationsAreReadableAndKeepActualConnectedSeconds(double seconds, string expected) =>
        Assert.That(VoiceActivityTracker.Duration(seconds), Is.EqualTo(expected));
}
