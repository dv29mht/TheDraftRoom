using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FcDraft.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncementsAndCampaignDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "payload",
                table: "email_outbox",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "campaign_id",
                table: "email_outbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    audience = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    draft_id = table.Column<Guid>(type: "uuid", nullable: true),
                    audience_label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    recipient_count = table.Column<int>(type: "integer", nullable: false),
                    email_count = table.Column<int>(type: "integer", nullable: false),
                    opted_out_count = table.Column<int>(type: "integer", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcements", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_outbox_campaign_id",
                table: "email_outbox",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_announcements_requested_at",
                table: "announcements",
                column: "requested_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "announcements");

            migrationBuilder.DropIndex(
                name: "ix_email_outbox_campaign_id",
                table: "email_outbox");

            migrationBuilder.DropColumn(
                name: "campaign_id",
                table: "email_outbox");

            migrationBuilder.AlterColumn<string>(
                name: "payload",
                table: "email_outbox",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096,
                oldNullable: true);
        }
    }
}
