using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MrClean.Migrations
{
    public partial class AddRetentionFilter : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FilterId",
                table: "MessageRetentionRules",
                type: "integer",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilterId",
                table: "MessageRetentionRules");
        }
    }
}
