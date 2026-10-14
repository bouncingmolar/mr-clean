using System.Text.RegularExpressions;
using Discord;
using MrClean.Services;
using NUnit.Framework;

namespace MrClean.Tests.Services;

public class MessageFilteringServiceTests
{
    private const string TestPattern = "MR_CLEAN_EMBED_TEST";

    [Test]
    public void MatchesEmbedTitle()
    {
        var embed = new EmbedBuilder()
            .WithTitle(TestPattern)
            .Build();

        Assert.That(
            MessageFilteringService.MatchesEmbed(new Regex(TestPattern), embed),
            Is.True);
    }

    [Test]
    public void MatchesEmbedDescription()
    {
        var embed = new EmbedBuilder()
            .WithDescription(TestPattern)
            .Build();

        Assert.That(
            MessageFilteringService.MatchesEmbed(new Regex(TestPattern), embed),
            Is.True);
    }

    [Test]
    public void MatchesEmbedFieldName()
    {
        var embed = new EmbedBuilder()
            .AddField(TestPattern, "ordinary value")
            .Build();

        Assert.That(
            MessageFilteringService.MatchesEmbed(new Regex(TestPattern), embed),
            Is.True);
    }

    [Test]
    public void MatchesEmbedFieldValue()
    {
        var embed = new EmbedBuilder()
            .AddField("ordinary field", TestPattern)
            .Build();

        Assert.That(
            MessageFilteringService.MatchesEmbed(new Regex(TestPattern), embed),
            Is.True);
    }

    [Test]
    public void MatchesEmbedAuthor()
    {
        var embed = new EmbedBuilder()
            .WithAuthor(TestPattern)
            .Build();

        Assert.That(
            MessageFilteringService.MatchesEmbed(new Regex(TestPattern), embed),
            Is.True);
    }

    [Test]
    public void MatchesEmbedFooter()
    {
        var embed = new EmbedBuilder()
            .WithFooter(TestPattern)
            .Build();

        Assert.That(
            MessageFilteringService.MatchesEmbed(new Regex(TestPattern), embed),
            Is.True);
    }

    [Test]
    public void MatchesEmbedUrl()
    {
        var embed = new EmbedBuilder()
            .WithTitle("ordinary title")
            .WithUrl("https://example.com/MR_CLEAN_EMBED_TEST")
            .Build();

        Assert.That(
            MessageFilteringService.MatchesEmbed(new Regex(TestPattern), embed),
            Is.True);
    }

    [Test]
    public void DoesNotMatchUnrelatedEmbed()
    {
        var embed = new EmbedBuilder()
            .WithTitle("ordinary title")
            .WithDescription("ordinary description")
            .WithAuthor("ordinary author")
            .AddField("ordinary field", "ordinary value")
            .WithFooter("ordinary footer")
            .Build();

        Assert.That(
            MessageFilteringService.MatchesEmbed(new Regex(TestPattern), embed),
            Is.False);
    }
}
