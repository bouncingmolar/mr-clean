namespace MrClean.Configuration;

public class VoiceLoggingOptions
{
    public bool Enabled { get; set; }
    public ulong LogChannelId { get; set; }
    public ulong GeneralChannelId { get; set; }
    public int ReconnectWindowSeconds { get; set; } = 120;
    // ConfigurationBinder in .NET 6 requires string dictionary keys for Discord snowflakes.
    public Dictionary<string, string> Channels { get; set; } = new();

    // Overrides keyed by source voice channel ID; labels remain in Channels.
    public Dictionary<string, ulong> ChannelLogDestinations { get; set; } = new();

    public ulong Destination(ulong channelId) => ChannelLogDestinations.TryGetValue(channelId.ToString(System.Globalization.CultureInfo.InvariantCulture), out var destination)
        ? destination : LogChannelId;

    public string? Activity(ulong channelId, string name) => channelId == GeneralChannelId && channelId != 0
        ? name : Channels.TryGetValue(channelId.ToString(System.Globalization.CultureInfo.InvariantCulture), out var label) ? label : null;
}
