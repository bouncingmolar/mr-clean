namespace MrClean.Models;

public class RoleLoggingConfiguration
{
    public int Id { get; set; }
    public bool Enabled { get; set; } = false;

    // Any role without a specific route is logged here.
    public ulong DefaultChannelId { get; set; }

    public List<RoleLogRoute> Routes { get; set; } = new();
}

public class RoleLogRoute
{
    public int Id { get; set; }
    public ulong RoleId { get; set; }
    public ulong ChannelId { get; set; }

    public int RoleLoggingConfigurationId { get; set; }
    public RoleLoggingConfiguration Configuration { get; set; } = null!;
}
