using Discord;
using MrClean.Models;
using MrClean.Services.Filters;

namespace MrClean.Services.Retention;

public class MessageRetentionScanner
{
    private const int PageSize = 100;
    private readonly IMessageFiltersService _filters;

    public MessageRetentionScanner(IMessageFiltersService filters)
    {
        _filters = filters;
    }

    public async Task<RetentionScanResult> ScanAsync(
        IMessageChannel channel,
        MessageRetentionRule rule,
        DateTimeOffset? now = null)
    {
        var result = new RetentionScanResult();
        var humanReplyTargets = new HashSet<ulong>();
        var scanTime = now ?? DateTimeOffset.UtcNow;
        ulong? beforeMessageId = null;

        MessageFilter? filter = null;

        if (rule.FilterId.HasValue)
            filter = await _filters.GetMessageFilterAsync(rule.FilterId.Value);

        // Safety: a retention rule without a filter never selects messages.
        if (filter == null)
            return result;

        while (true)
        {
            IEnumerable<IMessage> messages;

            if (beforeMessageId.HasValue)
            {
                messages = await channel
                    .GetMessagesAsync(
                        beforeMessageId.Value,
                        Direction.Before,
                        PageSize,
                        CacheMode.AllowDownload)
                    .FlattenAsync();
            }
            else
            {
                messages = await channel
                    .GetMessagesAsync(
                        PageSize,
                        CacheMode.AllowDownload)
                    .FlattenAsync();
            }

            var page = messages.ToList();

            if (page.Count == 0)
                break;

            var ordered = page
                .OrderByDescending(m => m.Timestamp)
                .ToList();

            foreach (var message in ordered)
            {
                result.MessagesScanned++;

                if (result.Authors.TryGetValue(message.Author.Id, out var authorStats))
                    result.Authors[message.Author.Id] =
                        (authorStats.Name, authorStats.IsBot, authorStats.Count + 1);
                else
                    result.Authors[message.Author.Id] =
                        (message.Author.Username, message.Author.IsBot, 1);

                // Record human replies while walking backwards through history.
                if (!message.Author.IsBot &&
                    message.Reference != null &&
                    message.Reference.MessageId.IsSpecified)
                {
                    humanReplyTargets.Add(message.Reference.MessageId.Value);
                }

                if (!rule.MatchesAuthor(message))
                    continue;

                result.TargetAuthorMessages++;

                var cutoff = scanTime.AddDays(-rule.MaxAgeDays);

                if (message.Timestamp > cutoff)
                    continue;

                result.OldEnoughMessages++;

                if (!MessageFilteringService.MatchesPattern(filter, message))
                {
                    result.FilterMisses++;

                    if (result.FilterMissSamples.Count < 10)
                    {
                        var parts = new List<string>();

                        if (!string.IsNullOrWhiteSpace(message.Content))
                            parts.Add(message.Content);

                        foreach (var embed in message.Embeds)
                        {
                            if (!string.IsNullOrWhiteSpace(embed.Title))
                                parts.Add(embed.Title);

                            if (!string.IsNullOrWhiteSpace(embed.Description))
                                parts.Add(embed.Description);

                            foreach (var field in embed.Fields)
                            {
                                if (!string.IsNullOrWhiteSpace(field.Name))
                                    parts.Add(field.Name);

                                if (!string.IsNullOrWhiteSpace(field.Value))
                                    parts.Add(field.Value);
                            }
                        }

                        var text = string.Join(" | ", parts)
                            .Replace("\r", " ")
                            .Replace("\n", " ");

                        if (text.Length > 250)
                            text = text[..247] + "...";

                        if (string.IsNullOrWhiteSpace(text))
                            text = "(no visible text)";

                        result.FilterMissSamples.Add(
                            $"`{message.Id}` — {text}");
                    }

                    continue;
                }

                result.FilterMatches++;

                var expiry = MessageExpiryParser.GetExpiry(message);

                if (!expiry.HasValue)
                {
                    result.MissingExpiry++;
                    continue;
                }

                if (expiry.Value > scanTime)
                {
                    result.NotYetExpired++;
                    continue;
                }

                // Any reaction means somebody interacted with the post.
                // Be conservative and preserve it.
                if (message.Reactions.Count > 0)
                {
                    result.PreservedForReaction.Add(message);
                    continue;
                }

                if (rule.PreserveHumanReplies &&
                    humanReplyTargets.Contains(message.Id))
                {
                    result.PreservedForHumanReply.Add(message);
                }
                else
                {
                    result.DeleteCandidates.Add(message);
                }

                if (result.MessagesScanned % 500 == 0)
                    Console.WriteLine(
                        $"Retention scan: {result.MessagesScanned} scanned, " +
                        $"{result.DeleteCandidates.Count} candidates");
            }

            if (page.Count < PageSize)
                break;

            beforeMessageId = ordered[^1].Id;
        }

        return result;
    }
}
