using Discord;

namespace MrClean.Models;

public class ActivityRule
{
    public int Id { get; set; }
    public ulong GuildId { get; set; }
    public string Name { get; set; } = "";
    public ActivityType Type { get; set; }
    public ulong? ApplicationId { get; set; }
    public ulong RoleId { get; set; }
    public bool Enabled { get; set; } = true;
}

public class ActivityFallback
{
    public ulong GuildId { get; set; }
    public ActivityType Type { get; set; }
    public ulong RoleId { get; set; }
    public bool Enabled { get; set; }
}

public class ActivityGuildSettings
{
    public ulong GuildId { get; set; }
    // False until an administrator explicitly confirms cutover from the other role manager.
    public bool Enabled { get; set; }
}

// Minimal last role contribution only: no persisted activity names, presence history or details.
public class ActivityMemberState
{
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public string RoleIdsJson { get; set; } = "[]";
}
