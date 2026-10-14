using Discord;

namespace MrClean.Models;

public class MessageRetentionRule
{
    public int Id { get; set; }

    public bool Enabled { get; set; } = false;

    public ulong ChannelId { get; set; }

    public ulong AuthorId { get; set; }

    public string? WebhookUsername { get; set; }

    public bool MatchesAuthor(IMessage message)
    {
        if (!string.IsNullOrWhiteSpace(WebhookUsername))
        {
            return message.Author.IsWebhook &&
                   message.Author.Username.Equals(
                       WebhookUsername,
                       StringComparison.OrdinalIgnoreCase);
        }

        return message.Author.Id == AuthorId;
    }

    public int MaxAgeDays { get; set; } = 30;

    public int? FilterId { get; set; }

    public bool PreserveHumanReplies { get; set; } = true;

    public Embed Embed
    {
        get
        {
            var emoji = Enabled ? "🟢" : "🔴";

            return new EmbedBuilder()
                .WithColor(0x5865F2)
                .WithTitle($"{emoji} Retention rule #{Id}")
                .WithFooter("This retention rule is now " + (Enabled ? "enabled" : "disabled"))
                .AddField(
                    "Target",
                    !string.IsNullOrWhiteSpace(WebhookUsername)
                        ? $"Webhook named `{WebhookUsername}`"
                        : $"<@{AuthorId}> (`{AuthorId}`)",
                    true)
                .AddField("Channel", $"<#{ChannelId}>", true)
                .AddField("Delete after", $"{MaxAgeDays} days", true)
                .AddField("Filter", FilterId.HasValue ? $"#{FilterId}" : "None", true)
                .AddField("Preserve human replies", PreserveHumanReplies ? "Yes" : "No", true)
                .Build();
        }
    }
}
