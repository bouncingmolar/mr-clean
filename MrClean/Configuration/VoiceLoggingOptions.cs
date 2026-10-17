namespace MrClean.Configuration;

public class VoiceLoggingOptions
{
    public ulong ConnectedRoleId { get; set; }
    public bool Enabled { get; set; }
    // Fallback for old persisted sessions; new destinations are database rule settings.
    public ulong LogChannelId { get; set; }
    public int ReconnectWindowSeconds { get; set; } = 120;
}
