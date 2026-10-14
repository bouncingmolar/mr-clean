using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MrClean.Migrations
{
    public partial class AddRoleLogging : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoleLoggingConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    NormalChannelId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    NoisyChannelId = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleLoggingConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NoisyRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    RoleLoggingConfigurationId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoisyRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoisyRoles_RoleLoggingConfigurations_RoleLoggingConfigurati~",
                        column: x => x.RoleLoggingConfigurationId,
                        principalTable: "RoleLoggingConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoisyRoles_RoleLoggingConfigurationId",
                table: "NoisyRoles",
                column: "RoleLoggingConfigurationId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoisyRoles");

            migrationBuilder.DropTable(
                name: "RoleLoggingConfigurations");
        }
    }
}
