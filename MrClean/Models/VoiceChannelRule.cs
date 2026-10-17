using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace MrClean.Models;

public class VoiceChannelRule
{
    public int Id { get; set; }
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public bool Enabled { get; set; } = true;
    public string ActivityName { get; set; } = "Voice chat";
    public bool DynamicName { get; set; }
    // Optional {activity} placeholder, e.g. Playing {activity}.
    public string Wording { get; set; } = "{activity}";
    public bool LogJoin { get; set; } = true;
    public bool LogDeparture { get; set; } = true;
    public ulong DestinationId { get; set; }
    public bool TrackDuration { get; set; } = true;
    public bool TrackInterruptions { get; set; } = true;
    public string RoleIdsJson { get; set; } = "[]";
    [NotMapped] public ulong[] RoleIds
    {
        get => JsonSerializer.Deserialize<ulong[]>(RoleIdsJson) ?? Array.Empty<ulong>();
        set => RoleIdsJson = JsonSerializer.Serialize(value.Distinct().OrderBy(x => x));
    }
    public string Activity(string channelName) => Wording.Replace("{activity}", DynamicName ? channelName : ActivityName);
}

public class VoiceRoleOwnership
{
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public ulong RoleId { get; set; }
    // False is an acquisition intent, not permission to remove the role.
    public bool Confirmed { get; set; }
}
