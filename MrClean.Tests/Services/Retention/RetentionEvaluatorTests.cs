using System;
using MrClean.Models;
using MrClean.Services.Retention;
using NUnit.Framework;

namespace MrClean.Tests.Services.Retention;

public class RetentionEvaluatorTests
{
    private const ulong TargetBotId = 100;
    private const ulong OtherBotId = 200;
    private const ulong HumanId = 300;

    private static readonly DateTimeOffset Now =
        new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static MessageRetentionRule CreateRule(bool preserveReplies = true)
    {
        return new MessageRetentionRule
        {
            Enabled = true,
            ChannelId = 500,
            AuthorId = TargetBotId,
            MaxAgeDays = 30,
            PreserveHumanReplies = preserveReplies
        };
    }

    [Test]
    public void OldTargetBotMessageIsDeleted()
    {
        var result = RetentionEvaluator.Evaluate(
            CreateRule(),
            TargetBotId,
            Now.AddDays(-31),
            false,
            Now);

        Assert.That(result, Is.EqualTo(RetentionDecision.Delete));
    }

    [Test]
    public void NewTargetBotMessageIsKept()
    {
        var result = RetentionEvaluator.Evaluate(
            CreateRule(),
            TargetBotId,
            Now.AddDays(-29),
            false,
            Now);

        Assert.That(result, Is.EqualTo(RetentionDecision.Keep));
    }

    [Test]
    public void OldDifferentBotMessageIsKept()
    {
        var result = RetentionEvaluator.Evaluate(
            CreateRule(),
            OtherBotId,
            Now.AddDays(-31),
            false,
            Now);

        Assert.That(result, Is.EqualTo(RetentionDecision.Keep));
    }

    [Test]
    public void OldHumanMessageIsKept()
    {
        var result = RetentionEvaluator.Evaluate(
            CreateRule(),
            HumanId,
            Now.AddDays(-31),
            false,
            Now);

        Assert.That(result, Is.EqualTo(RetentionDecision.Keep));
    }

    [Test]
    public void OldTargetBotMessageWithHumanReplyIsPreserved()
    {
        var result = RetentionEvaluator.Evaluate(
            CreateRule(),
            TargetBotId,
            Now.AddDays(-31),
            true,
            Now);

        Assert.That(result, Is.EqualTo(RetentionDecision.PreserveHumanReply));
    }

    [Test]
    public void HumanReplyDoesNotPreserveWhenOptionIsDisabled()
    {
        var result = RetentionEvaluator.Evaluate(
            CreateRule(false),
            TargetBotId,
            Now.AddDays(-31),
            true,
            Now);

        Assert.That(result, Is.EqualTo(RetentionDecision.Delete));
    }

    [Test]
    public void MessageExactlyAtCutoffIsDeleted()
    {
        var result = RetentionEvaluator.Evaluate(
            CreateRule(),
            TargetBotId,
            Now.AddDays(-30),
            false,
            Now);

        Assert.That(result, Is.EqualTo(RetentionDecision.Delete));
    }
}
