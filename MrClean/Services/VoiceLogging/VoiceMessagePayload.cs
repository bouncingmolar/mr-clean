using System.Text.Json;
using Discord;

namespace MrClean.Services.VoiceLogging;

public class VoiceMessagePayload
{
    private const string Prefix = "voice:v2:";
    public string? Content { get; set; }
    public string? Description { get; set; }
    public ulong? HistoryBaseline { get; set; }
    public string Serialize() => Prefix + JsonSerializer.Serialize(this);
    public static VoiceMessagePayload Parse(string text) => text.StartsWith(Prefix, StringComparison.Ordinal)
        ? JsonSerializer.Deserialize<VoiceMessagePayload>(text[Prefix.Length..])!
        : new VoiceMessagePayload { Content = text }; // Preserve already queued v1 messages verbatim.
    public Embed? BuildEmbed() => Description == null ? null : new EmbedBuilder().WithDescription(Description).Build();
    public bool Matches(ulong id, string? content, IEnumerable<string?> descriptions) =>
        (HistoryBaseline == null || id > HistoryBaseline.Value) &&
        (content ?? "") == (Content ?? "") &&
        (Description == null ? !descriptions.Any() : descriptions.SequenceEqual(new[] { Description }));
}
