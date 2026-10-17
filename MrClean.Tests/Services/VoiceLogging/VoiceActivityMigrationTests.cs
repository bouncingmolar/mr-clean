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
