using Microsoft.EntityFrameworkCore;
using MrClean.Models;

namespace MrClean.Data;

#nullable disable
public class MrCleanDbContext : DbContext
{
    public MrCleanDbContext(DbContextOptions<MrCleanDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ActivityRule>().HasIndex(x => new { x.GuildId, x.Type, x.Name }).IsUnique();
        modelBuilder.Entity<ActivityFallback>().HasKey(x => new { x.GuildId, x.Type });
        modelBuilder.Entity<ActivityGuildSettings>().HasKey(x => x.GuildId);
        modelBuilder.Entity<ActivityMemberState>().HasKey(x => new { x.GuildId, x.UserId });
        modelBuilder.Entity<VoiceChannelRule>().HasIndex(x => new { x.GuildId, x.ChannelId }).IsUnique();
        modelBuilder.Entity<VoiceRoleOwnership>().HasKey(x => new { x.GuildId, x.UserId, x.RoleId });
        modelBuilder.Entity<VoiceLogMessage>().Property(x => x.Id).ValueGeneratedNever();
        modelBuilder.Entity<VoiceLogMessage>().HasIndex(x => x.Destination);
    }

    public virtual DbSet<ActivityRule> ActivityRules { get; set; }
    public virtual DbSet<ActivityFallback> ActivityFallbacks { get; set; }
    public virtual DbSet<ActivityGuildSettings> ActivityGuildSettings { get; set; }
    public virtual DbSet<ActivityMemberState> ActivityMemberStates { get; set; }

    public virtual DbSet<VoiceChannelRule> VoiceChannelRules { get; set; }
    public virtual DbSet<VoiceRoleOwnership> VoiceRoleOwnerships { get; set; }

    public virtual DbSet<VoiceLogMessage> VoiceLogMessages { get; set; }

    public virtual DbSet<VoiceActivityState> VoiceActivityStates { get; set; }

    public virtual DbSet<MessageFilter> MessageFilters { get; set; }

    public virtual DbSet<MessageRetentionRule> MessageRetentionRules { get; set; }

    public virtual DbSet<RoleLoggingConfiguration> RoleLoggingConfigurations { get; set; }

    public virtual DbSet<RoleLogRoute> RoleLogRoutes { get; set; }
}