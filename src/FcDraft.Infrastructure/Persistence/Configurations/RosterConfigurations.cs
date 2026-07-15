using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FcDraft.Infrastructure.Persistence.Configurations;

/// <summary>Explicit snake_case mappings for versioned roster templates and their ordered slots (PR-09).</summary>
public sealed class RosterTemplateConfiguration : IEntityTypeConfiguration<RosterTemplate>
{
    public void Configure(EntityTypeBuilder<RosterTemplate> builder)
    {
        builder.ToTable("roster_templates");
        builder.HasKey(template => template.Id);
        builder.Property(template => template.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(template => template.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        builder.Property(template => template.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(template => template.PickTimerSeconds).HasColumnName("pick_timer_seconds").IsRequired();
        builder.Property(template => template.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasMany(template => template.Slots)
            .WithOne()
            .HasForeignKey(slot => slot.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(template => template.IsActive).HasDatabaseName("ix_roster_templates_is_active");
    }
}

public sealed class RosterSlotConfiguration : IEntityTypeConfiguration<RosterSlot>
{
    public void Configure(EntityTypeBuilder<RosterSlot> builder)
    {
        builder.ToTable("roster_slots");
        builder.HasKey(slot => slot.Id);
        builder.Property(slot => slot.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(slot => slot.TemplateId).HasColumnName("template_id").IsRequired();
        builder.Property(slot => slot.Order).HasColumnName("slot_order").IsRequired();
        builder.Property(slot => slot.SlotType).HasColumnName("slot_type").HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(slot => slot.Position).HasColumnName("position").HasMaxLength(8);
        builder.Property(slot => slot.Label).HasColumnName("label").HasMaxLength(64).IsRequired();

        builder.HasIndex(slot => new { slot.TemplateId, slot.Order })
            .HasDatabaseName("ix_roster_slots_template_order")
            .IsUnique();
    }
}
