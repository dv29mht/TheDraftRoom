using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FcDraft.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "roster_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    pick_timer_seconds = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roster_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roster_slots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot_order = table.Column<int>(type: "integer", nullable: false),
                    slot_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    position = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roster_slots", x => x.id);
                    table.ForeignKey(
                        name: "FK_roster_slots_roster_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "roster_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_roster_slots_template_order",
                table: "roster_slots",
                columns: new[] { "template_id", "slot_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roster_templates_is_active",
                table: "roster_templates",
                column: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "roster_slots");

            migrationBuilder.DropTable(
                name: "roster_templates");
        }
    }
}
