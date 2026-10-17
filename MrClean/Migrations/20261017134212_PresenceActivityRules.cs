using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MrClean.Migrations
{
    public partial class PresenceActivityRules : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityFallbacks",
                columns: table => new
                {
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    RoleId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityFallbacks", x => new { x.GuildId, x.Type });
                });

            migrationBuilder.CreateTable(
                name: "ActivityGuildSettings",
                columns: table => new
                {
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityGuildSettings", x => x.GuildId);
                });

            migrationBuilder.CreateTable(
                name: "ActivityMemberStates",
                columns: table => new
                {
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    UserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    RoleIdsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityMemberStates", x => new { x.GuildId, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "ActivityRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ApplicationId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    RoleId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityRules_GuildId_Type_Name",
                table: "ActivityRules",
                columns: new[] { "GuildId", "Type", "Name" },
                unique: true);
            migrationBuilder.Sql(@"
-- Explicitly verified existing role IDs; configuration only, no assignments or new Discord roles.
INSERT INTO ""ActivityGuildSettings"" (""GuildId"",""Enabled"") VALUES (683633975838769192,false);
INSERT INTO ""ActivityFallbacks"" (""GuildId"",""Type"",""RoleId"",""Enabled"") VALUES
(683633975838769192,0,1069145493852131329,false),
(683633975838769192,2,1069145494674214973,false),
(683633975838769192,3,1069145496054153236,false),
(683633975838769192,1,1069145496985284648,false);
");

        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityFallbacks");

            migrationBuilder.DropTable(
                name: "ActivityGuildSettings");

            migrationBuilder.DropTable(
                name: "ActivityMemberStates");

            migrationBuilder.DropTable(
                name: "ActivityRules");
        }
    }
}
