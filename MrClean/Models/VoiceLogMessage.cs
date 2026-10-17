namespace MrClean.Models;

public class VoiceLogMessage
{
    public long Id { get; set; }
    public ulong Destination { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset? AttemptedAt { get; set; }
}
