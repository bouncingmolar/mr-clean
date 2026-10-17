using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MrClean.Data;

// Schema tooling never starts the bot or reads deployment credentials.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MrCleanDbContext>
{
    public MrCleanDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<MrCleanDbContext>()
        .UseNpgsql("Host=localhost;Database=mr_clean_schema_only;Username=unused").Options);
}
