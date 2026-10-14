using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MrClean.Migrations
{
    public partial class GeneralizeRoleLogRouting : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoisyRoles");

            migrationBuilder.DropColumn(
                name: "NoisyChannelId",
                table: "RoleLoggingConfigurations");

            migrationBuilder.RenameColumn(
                name: "NormalChannelId",
                table: "RoleLoggingConfigurations",
                newName: "DefaultChannelId");

            migrationBuilder.CreateTable(
                name: "RoleLogRoutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    ChannelId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    RoleLoggingConfigurationId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleLogRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleLogRoutes_RoleLoggingConfigurations_RoleLoggingConfigur~",
                        column: x => x.RoleLoggingConfigurationId,
                        principalTable: "RoleLoggingConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoleLogRoutes_RoleLoggingConfigurationId",
                table: "RoleLogRoutes",
                column: "RoleLoggingConfigurationId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleLogRoutes");

            migrationBuilder.RenameColumn(
                name: "DefaultChannelId",
                table: "RoleLoggingConfigurations",
                newName: "NormalChannelId");

            migrationBuilder.AddColumn<decimal>(
                name: "NoisyChannelId",
                table: "RoleLoggingConfigurations",
                type: "numeric(20,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "NoisyRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleLoggingConfigurationId = table.Column<int>(type: "integer", nullable: false),
                    RoleId = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
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
    }
}
