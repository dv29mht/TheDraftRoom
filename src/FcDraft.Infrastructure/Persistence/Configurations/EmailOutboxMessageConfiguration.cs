using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FcDraft.Infrastructure.Persistence.Configurations;

/// <summary>
/// Explicit snake_case mapping for the durable email outbox. Indexed by (status, next_attempt_at)
/// so the worker's "due pending messages" query is efficient.
/// </summary>
public sealed class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("email_outbox");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(message => message.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(message => message.ToEmail)
            .HasColumnName("to_email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(message => message.ToName)
            .HasColumnName("to_name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(message => message.Secret)
            .HasColumnName("secret")
            .HasMaxLength(512);

        builder.Property(message => message.Payload)
            .HasColumnName("payload")
            .HasMaxLength(2048);

        builder.Property(message => message.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(message => message.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired();

        builder.Property(message => message.MaxAttempts)
            .HasColumnName("max_attempts")
            .IsRequired();

        builder.Property(message => message.NextAttemptAt)
            .HasColumnName("next_attempt_at")
            .IsRequired();

        builder.Property(message => message.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(1024);

        builder.Property(message => message.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(message => message.SentAt)
            .HasColumnName("sent_at");

        builder.HasIndex(message => new { message.Status, message.NextAttemptAt })
            .HasDatabaseName("ix_email_outbox_status_next_attempt_at");
    }
}
