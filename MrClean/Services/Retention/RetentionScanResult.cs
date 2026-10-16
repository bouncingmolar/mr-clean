using Discord;

namespace MrClean.Services.Retention;

public class RetentionScanResult
{
    public List<IMessage> DeleteCandidates { get; } = new();

    public List<IMessage> PreservedForHumanReply { get; } = new();

    public List<IMessage> PreservedForReaction { get; } = new();

    public int MessagesScanned { get; set; }
    public int TargetAuthorMessages { get; set; }
    public int OldEnoughMessages { get; set; }
    public int FilterMatches { get; set; }
    public int FilterMisses { get; set; }
    public List<string> FilterMissSamples { get; } = new();
    public int MissingExpiry { get; set; }
    public int NotYetExpired { get; set; }
    public Dictionary<ulong, (string Name, bool IsBot, int Count)> Authors { get; } = new();
}
