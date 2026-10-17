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
        modelBuilder.Entity<VoiceLogMessage>().Property(x => x.Id).ValueGeneratedNever();
        modelBuilder.Entity<VoiceLogMessage>().HasIndex(x => x.Destination);
    }

    public virtual DbSet<VoiceLogMessage> VoiceLogMessages { get; set; }

    public virtual DbSet<VoiceActivityState> VoiceActivityStates { get; set; }

    public virtual DbSet<MessageFilter> MessageFilters { get; set; }

    public virtual DbSet<MessageRetentionRule> MessageRetentionRules { get; set; }

    public virtual DbSet<RoleLoggingConfiguration> RoleLoggingConfigurations { get; set; }

    public virtual DbSet<RoleLogRoute> RoleLogRoutes { get; set; }
}