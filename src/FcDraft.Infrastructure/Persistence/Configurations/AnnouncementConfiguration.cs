using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FcDraft.Infrastructure.Persistence.Configurations;

/// <summary>
/// Explicit snake_case mapping for the append-only announcement campaign records (PR-21, §9.8).
/// No foreign keys: like the security audit trail, a campaign record must survive independently of
/// the draft or admin account it references. Indexed by request time for the newest-first list.
/// </summary>
public sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("announcements");

        builder.HasKey(announcement => announcement.Id);

        builder.Property(announcement => announcement.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(announcement => announcement.Subject)
            .HasColumnName("subject")
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(announcement => announcement.Body)
            .HasColumnName("body")
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(announcement => announcement.Audience)
            .HasColumnName("audience")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(announcement => announcement.DraftId)
            .HasColumnName("draft_id");

        builder.Property(announcement => announcement.AudienceLabel)
            .HasColumnName("audience_label")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(announcement => announcement.RecipientCount)
            .HasColumnName("recipient_count")
            .IsRequired();

        builder.Property(announcement => announcement.EmailCount)
            .HasColumnName("email_count")
            .IsRequired();

        builder.Property(announcement => announcement.OptedOutCount)
            .HasColumnName("opted_out_count")
            .IsRequired();

        builder.Property(announcement => announcement.RequestedByUserId)
            .HasColumnName("requested_by_user_id")
            .IsRequired();

        builder.Property(announcement => announcement.RequestedByEmail)
            .HasColumnName("requested_by_email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(announcement => announcement.RequestedAt)
            .HasColumnName("requested_at")
            .IsRequired();

        builder.HasIndex(announcement => announcement.RequestedAt)
            .HasDatabaseName("ix_announcements_requested_at");
    }
}
