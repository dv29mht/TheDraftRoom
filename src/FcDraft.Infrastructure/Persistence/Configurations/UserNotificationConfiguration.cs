using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FcDraft.Infrastructure.Persistence.Configurations;

/// <summary>
/// Explicit snake_case mapping for the persistent per-user notifications (PR-20). Indexed by
/// (user_id, created_at) for the newest-first list and (user_id, read_at) for the unread badge count.
/// </summary>
public sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> builder)
    {
        builder.ToTable("user_notifications");

        builder.HasKey(notification => notification.Id);

        builder.Property(notification => notification.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(notification => notification.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(notification => notification.Type)
            .HasColumnName("type")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(notification => notification.Title)
            .HasColumnName("title")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(notification => notification.Body)
            .HasColumnName("body")
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(notification => notification.DraftId)
            .HasColumnName("draft_id");

        builder.Property(notification => notification.ReadAt)
            .HasColumnName("read_at");

        builder.Property(notification => notification.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(notification => new { notification.UserId, notification.CreatedAt })
            .HasDatabaseName("ix_user_notifications_user_created");

        builder.HasIndex(notification => new { notification.UserId, notification.ReadAt })
            .HasDatabaseName("ix_user_notifications_user_read");

        // Notifications reference their user; deleting an account removes its inbox with it.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(notification => notification.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
