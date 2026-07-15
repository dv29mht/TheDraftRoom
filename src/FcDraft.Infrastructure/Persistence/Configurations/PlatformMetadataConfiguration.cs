using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FcDraft.Infrastructure.Persistence.Configurations;

/// <summary>Explicit snake_case mapping for the key/value platform metadata table.</summary>
public sealed class PlatformMetadataConfiguration : IEntityTypeConfiguration<PlatformMetadata>
{
    public void Configure(EntityTypeBuilder<PlatformMetadata> builder)
    {
        builder.ToTable("platform_metadata");

        builder.HasKey(metadata => metadata.Key);

        builder.Property(metadata => metadata.Key)
            .HasColumnName("key")
            .HasMaxLength(128)
            .ValueGeneratedNever();

        builder.Property(metadata => metadata.Value)
            .HasColumnName("value")
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(metadata => metadata.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
    }
}
