using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FcDraft.Infrastructure.Persistence.Configurations;

/// <summary>
/// Explicit snake_case mapping for the append-only security-audit trail. Indexed by time for the
/// admin views that arrive in PR-21. No foreign key to users: audit rows must survive independently
/// of the account they reference and a failed sign-in may reference no account at all.
/// </summary>
public sealed class SecurityAuditEventConfiguration : IEntityTypeConfiguration<SecurityAuditEvent>
{
    public void Configure(EntityTypeBuilder<SecurityAuditEvent> builder)
    {
        builder.ToTable("security_audit_events");

        builder.HasKey(audit => audit.Id);

        builder.Property(audit => audit.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(audit => audit.UserId)
            .HasColumnName("user_id");

        builder.Property(audit => audit.Email)
            .HasColumnName("email")
            .HasMaxLength(320);

        builder.Property(audit => audit.Action)
            .HasColumnName("action")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(audit => audit.Detail)
            .HasColumnName("detail")
            .HasMaxLength(512);

        builder.Property(audit => audit.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(64);

        builder.Property(audit => audit.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(audit => audit.CreatedAt)
            .HasDatabaseName("ix_security_audit_events_created_at");
    }
}
