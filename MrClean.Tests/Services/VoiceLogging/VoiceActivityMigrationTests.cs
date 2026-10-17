using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MrClean.Data;
using NUnit.Framework;

namespace MrClean.Tests.Services.VoiceLogging;

public class VoiceActivityMigrationTests
{
    [Test]
    public void MigrationOnlyAddsVoiceTables()
    {
        using var db = new MrCleanDbContext(new DbContextOptionsBuilder<MrCleanDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var sql = db.GetService<IMigrator>().GenerateScript("20261006073947_GeneralizeRoleLogRouting",
            "20261017120000_AddVoiceActivityState");
        Assert.That(sql, Does.Contain("CREATE TABLE \"VoiceActivityStates\""));
        Assert.That(sql, Does.Contain("CREATE TABLE \"VoiceLogMessages\""));
        Assert.That(sql, Does.Not.Contain("DROP TABLE").And.Not.Contain("ALTER TABLE"));
    }

    [Test]
    public void RulesMigrationSeedsFourRulesWithoutRewritingVoiceSessionsAndDerivesConnectedRoute()
    {
        using var db = new MrCleanDbContext(new DbContextOptionsBuilder<MrCleanDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var sql = db.GetService<IMigrator>().GenerateScript("20261017120000_AddVoiceActivityState", "20261017130932_VoiceChannelRules");
        foreach (var id in new[] { "798528429091061780", "792285032713945119", "1126920146099912744", "1126902663607484476" })
            Assert.That(sql, Does.Contain(id));
        Assert.That(sql, Does.Contain("[]").And.Contain("894787914599370803").And.Contain("1069145494674214973"));
        Assert.That(sql, Does.Not.Contain("UPDATE \"VoiceActivityStates\"").And.Not.Contain("DELETE FROM \"VoiceLogMessages\""));
        Assert.That(sql, Does.Contain("count(DISTINCT \"ChannelId\")"));
    }

    [Test]
    public void PresenceMigrationOnlyAddsConfigurationAndDisabledVerifiedFallbacks()
    {
        using var db = new MrCleanDbContext(new DbContextOptionsBuilder<MrCleanDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var sql = db.GetService<IMigrator>().GenerateScript("20261017130932_VoiceChannelRules", "20261017134212_PresenceActivityRules");
        foreach (var id in new[] { "1069145493852131329", "1069145494674214973", "1069145496054153236", "1069145496985284648" })
            Assert.That(sql, Does.Contain(id));
        Assert.That(sql, Does.Contain("false").And.Not.Contain("UPDATE \"VoiceActivityStates\"").And.Not.Contain("DELETE FROM \"VoiceLogMessages\""));
        Assert.That(sql, Does.Not.Contain("UPDATE \"RoleLogRoutes\""));
    }

    [Test]
    public void SnapshotMatchesRuntimeModel()
    {
        using var db = new MrCleanDbContext(new DbContextOptionsBuilder<MrCleanDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var initialized = db.GetService<Microsoft.EntityFrameworkCore.Infrastructure.IModelRuntimeInitializer>()
            .Initialize(snapshot, designTime: true);
        var differences = db.GetService<IMigrationsModelDiffer>().GetDifferences(
            initialized.GetRelationalModel(), db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetRelationalModel());
        Assert.That(differences, Is.Empty);
    }
}
