using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FcDraft.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerDataset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_dataset_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    footballer_count = table.Column<int>(type: "integer", nullable: false),
                    club_count = table.Column<int>(type: "integer", nullable: false),
                    error_count = table.Column<int>(type: "integer", nullable: false),
                    warning_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_dataset_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clubs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    league = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    star_rating = table.Column<int>(type: "integer", nullable: true),
                    is_five_star_eligible = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clubs", x => x.id);
                    table.ForeignKey(
                        name: "FK_clubs_player_dataset_versions_dataset_version_id",
                        column: x => x.dataset_version_id,
                        principalTable: "player_dataset_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dataset_import_issues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    row = table.Column<int>(type: "integer", nullable: false),
                    external_id = table.Column<int>(type: "integer", nullable: true),
                    field = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dataset_import_issues", x => x.id);
                    table.ForeignKey(
                        name: "FK_dataset_import_issues_player_dataset_versions_dataset_versi~",
                        column: x => x.dataset_version_id,
                        principalTable: "player_dataset_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "footballers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<int>(type: "integer", nullable: false),
                    common_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    full_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    overall = table.Column<int>(type: "integer", nullable: false),
                    primary_position = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    club = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    league = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    nation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    preferred_foot = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    weak_foot = table.Column<int>(type: "integer", nullable: false),
                    skill_moves = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    source_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    is_kick_off_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    stats_json = table.Column<string>(type: "jsonb", nullable: false),
                    roles_json = table.Column<string>(type: "jsonb", nullable: false),
                    playstyles_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_footballers", x => x.id);
                    table.ForeignKey(
                        name: "FK_footballers_player_dataset_versions_dataset_version_id",
                        column: x => x.dataset_version_id,
                        principalTable: "player_dataset_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "footballer_positions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    footballer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_footballer_positions", x => x.id);
                    table.ForeignKey(
                        name: "FK_footballer_positions_footballers_footballer_id",
                        column: x => x.footballer_id,
                        principalTable: "footballers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_clubs_version_name",
                table: "clubs",
                columns: new[] { "dataset_version_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dataset_import_issues_version",
                table: "dataset_import_issues",
                column: "dataset_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_footballer_positions_footballer_id",
                table: "footballer_positions",
                column: "footballer_id");

            migrationBuilder.CreateIndex(
                name: "ix_footballer_positions_position",
                table: "footballer_positions",
                column: "position");

            migrationBuilder.CreateIndex(
                name: "ix_footballers_version_external_id",
                table: "footballers",
                columns: new[] { "dataset_version_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_footballers_version_overall",
                table: "footballers",
                columns: new[] { "dataset_version_id", "overall" });

            migrationBuilder.CreateIndex(
                name: "ix_player_dataset_versions_status",
                table: "player_dataset_versions",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clubs");

            migrationBuilder.DropTable(
                name: "dataset_import_issues");

            migrationBuilder.DropTable(
                name: "footballer_positions");

            migrationBuilder.DropTable(
                name: "footballers");

            migrationBuilder.DropTable(
                name: "player_dataset_versions");
        }
    }
}
