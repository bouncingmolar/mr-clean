using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MrClean.Data;

#nullable disable
namespace MrClean.Migrations;

[DbContext(typeof(MrCleanDbContext))]
[Migration("20261017120000_AddVoiceActivityState")]
public partial class AddVoiceActivityState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "VoiceActivityStates",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Json = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_VoiceActivityStates", x => x.Id));
        migrationBuilder.CreateTable(
            name: "VoiceLogMessages",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false),
                Destination = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                Text = table.Column<string>(type: "text", nullable: false),
                AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_VoiceLogMessages", x => x.Id));
        migrationBuilder.CreateIndex("IX_VoiceLogMessages_Destination", "VoiceLogMessages", "Destination");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("VoiceLogMessages");
        migrationBuilder.DropTable("VoiceActivityStates");
    }
}
