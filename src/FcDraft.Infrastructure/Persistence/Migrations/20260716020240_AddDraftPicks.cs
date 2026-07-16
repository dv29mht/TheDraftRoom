using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FcDraft.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftPicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "draft_picks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot_order = table.Column<int>(type: "integer", nullable: false),
                    footballer_id = table.Column<int>(type: "integer", nullable: false),
                    footballer_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    footballer_overall = table.Column<int>(type: "integer", nullable: false),
                    footballer_position = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    picked_by_participant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_draft_picks", x => x.id);
                    table.ForeignKey(
                        name: "FK_draft_picks_drafts_draft_id",
                        column: x => x.draft_id,
                        principalTable: "drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_draft_teams_draft_club",
                table: "draft_teams",
                columns: new[] { "draft_id", "selected_club_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_draft_picks_draft_footballer",
                table: "draft_picks",
                columns: new[] { "draft_id", "footballer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_draft_picks_team_slot",
                table: "draft_picks",
                columns: new[] { "draft_team_id", "slot_order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "draft_picks");

            migrationBuilder.DropIndex(
                name: "ix_draft_teams_draft_club",
                table: "draft_teams");
        }
    }
}
