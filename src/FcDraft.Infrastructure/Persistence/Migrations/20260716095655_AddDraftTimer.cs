using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FcDraft.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftTimer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paused_at",
                table: "drafts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "turn_started_at",
                table: "drafts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "paused_at",
                table: "drafts");

            migrationBuilder.DropColumn(
                name: "turn_started_at",
                table: "drafts");
        }
    }
}
