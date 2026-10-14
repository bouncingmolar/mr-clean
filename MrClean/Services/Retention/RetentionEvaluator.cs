using MrClean.Models;

namespace MrClean.Services.Retention;

public static class RetentionEvaluator
{
    public static RetentionDecision Evaluate(
        MessageRetentionRule rule,
        ulong messageAuthorId,
        DateTimeOffset messageTimestamp,
        bool hasHumanReply,
        DateTimeOffset now)
    {
        // A retention rule may only ever delete messages from its configured author.
        if (messageAuthorId != rule.AuthorId)
        {
            return RetentionDecision.Keep;
        }

        var cutoff = now.AddDays(-rule.MaxAgeDays);

        if (messageTimestamp > cutoff)
        {
            return RetentionDecision.Keep;
        }

        if (rule.PreserveHumanReplies && hasHumanReply)
        {
            return RetentionDecision.PreserveHumanReply;
        }

        return RetentionDecision.Delete;
    }
}
