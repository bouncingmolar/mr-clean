using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MrClean.Migrations
{
    public partial class AddRetentionWebhookUsername : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WebhookUsername",
                table: "MessageRetentionRules",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WebhookUsername",
                table: "MessageRetentionRules");
        }
    }
}
