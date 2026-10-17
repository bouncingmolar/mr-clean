using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MrClean.Migrations
{
    public partial class VoiceChannelRules : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VoiceChannelRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    ChannelId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ActivityName = table.Column<string>(type: "text", nullable: false),
                    DynamicName = table.Column<bool>(type: "boolean", nullable: false),
                    Wording = table.Column<string>(type: "text", nullable: false),
                    LogJoin = table.Column<bool>(type: "boolean", nullable: false),
                    LogDeparture = table.Column<bool>(type: "boolean", nullable: false),
                    DestinationId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    TrackDuration = table.Column<bool>(type: "boolean", nullable: false),
                    TrackInterruptions = table.Column<bool>(type: "boolean", nullable: false),
                    RoleIdsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceChannelRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VoiceRoleOwnerships",
                columns: table => new
                {
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    UserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    RoleId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Confirmed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceRoleOwnerships", x => new { x.GuildId, x.UserId, x.RoleId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoiceChannelRules_GuildId_ChannelId",
                table: "VoiceChannelRules",
                columns: new[] { "GuildId", "ChannelId" },
                unique: true);
            migrationBuilder.Sql(@"
INSERT INTO ""VoiceChannelRules"" (""GuildId"", ""ChannelId"", ""Enabled"", ""ActivityName"", ""DynamicName"", ""Wording"", ""LogJoin"", ""LogDeparture"", ""DestinationId"", ""TrackDuration"", ""TrackInterruptions"", ""RoleIdsJson"") VALUES
(683633975838769192,798528429091061780,true,'Voice',true,'{activity}',true,true,806802061693550652,true,true,'[]'),
(683633975838769192,792285032713945119,true,'Music',false,'{activity}',true,true,943558876706533477,true,true,'[]'),
(683633975838769192,1126920146099912744,true,'Gartic Phone',false,'{activity}',true,true,1556935771049689119,true,true,'[]'),
(683633975838769192,1126902663607484476,true,'Codenames',false,'{activity}',true,true,1556935771049689119,true,true,'[]');

-- Restore Connected logs via the EXISTING noisy-role route. Never assume a destination ID.
DO $$
DECLARE cfg integer; target numeric(20,0); destinations integer;
BEGIN
  FOR cfg IN SELECT ""Id"" FROM ""RoleLoggingConfigurations"" LOOP
    SELECT count(DISTINCT ""ChannelId""), min(""ChannelId"") INTO destinations,target FROM ""RoleLogRoutes""
      WHERE ""RoleLoggingConfigurationId""=cfg AND ""RoleId"" IN (1069145493852131329,1069145494674214973,1069145496054153236);
    IF destinations <> 1 THEN
      RAISE EXCEPTION 'Cannot determine the existing annoying-role route; configure Listening/Watching/Other Games consistently before migration';
    END IF;
    UPDATE ""RoleLogRoutes"" SET ""ChannelId""=target WHERE ""RoleLoggingConfigurationId""=cfg AND ""RoleId""=894787914599370803;
    IF NOT FOUND THEN
      INSERT INTO ""RoleLogRoutes"" (""RoleId"",""ChannelId"",""RoleLoggingConfigurationId"") VALUES (894787914599370803,target,cfg);
    END IF;
  END LOOP;
END; $$;
");

        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoiceChannelRules");

            migrationBuilder.DropTable(
                name: "VoiceRoleOwnerships");
        }
    }
}
