using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FcDraft.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    host_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    roster_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    pick_timer_seconds = table.Column<int>(type: "integer", nullable: false),
                    pinned_dataset_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drafts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "draft_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    from_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    to_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    payload = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_draft_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_draft_events_drafts_draft_id",
                        column: x => x.draft_id,
                        principalTable: "drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "draft_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_host = table.Column<bool>(type: "boolean", nullable: false),
                    seed = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_ready = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_draft_participants", x => x.id);
                    table.ForeignKey(
                        name: "FK_draft_participants_drafts_draft_id",
                        column: x => x.draft_id,
                        principalTable: "drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "draft_roster_slots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot_order = table.Column<int>(type: "integer", nullable: false),
                    slot_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    position = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_draft_roster_slots", x => x.id);
                    table.ForeignKey(
                        name: "FK_draft_roster_slots_drafts_draft_id",
                        column: x => x.draft_id,
                        principalTable: "drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "draft_teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    spinner_rank = table.Column<int>(type: "integer", nullable: true),
                    selected_club_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_draft_teams", x => x.id);
                    table.ForeignKey(
                        name: "FK_draft_teams_drafts_draft_id",
                        column: x => x.draft_id,
                        principalTable: "drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "draft_team_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_draft_team_members", x => x.id);
                    table.ForeignKey(
                        name: "FK_draft_team_members_draft_teams_draft_team_id",
                        column: x => x.draft_team_id,
                        principalTable: "draft_teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_draft_events_draft_sequence",
                table: "draft_events",
                columns: new[] { "draft_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_draft_participants_draft_user",
                table: "draft_participants",
                columns: new[] { "draft_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_draft_roster_slots_draft_order",
                table: "draft_roster_slots",
                columns: new[] { "draft_id", "slot_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_draft_team_members_draft_participant",
                table: "draft_team_members",
                columns: new[] { "draft_id", "participant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_draft_team_members_draft_team_id",
                table: "draft_team_members",
                column: "draft_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_draft_teams_draft_rank",
                table: "draft_teams",
                columns: new[] { "draft_id", "spinner_rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_drafts_code",
                table: "drafts",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_drafts_host_user_id",
                table: "drafts",
                column: "host_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_drafts_status",
                table: "drafts",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "draft_events");

            migrationBuilder.DropTable(
                name: "draft_participants");

            migrationBuilder.DropTable(
                name: "draft_roster_slots");

            migrationBuilder.DropTable(
                name: "draft_team_members");

            migrationBuilder.DropTable(
                name: "draft_teams");

            migrationBuilder.DropTable(
                name: "drafts");
        }
    }
}
