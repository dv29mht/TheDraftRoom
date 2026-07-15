using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FcDraft.Infrastructure.Persistence.Configurations;

/// <summary>Explicit snake_case mappings for the versioned footballer/club dataset (PR-07).</summary>
public sealed class PlayerDatasetVersionConfiguration : IEntityTypeConfiguration<PlayerDatasetVersion>
{
    public void Configure(EntityTypeBuilder<PlayerDatasetVersion> builder)
    {
        builder.ToTable("player_dataset_versions");
        builder.HasKey(version => version.Id);
        builder.Property(version => version.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(version => version.Label).HasColumnName("label").HasMaxLength(128).IsRequired();
        builder.Property(version => version.Source).HasColumnName("source").HasMaxLength(512).IsRequired();
        builder.Property(version => version.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(version => version.FootballerCount).HasColumnName("footballer_count").IsRequired();
        builder.Property(version => version.ClubCount).HasColumnName("club_count").IsRequired();
        builder.Property(version => version.ErrorCount).HasColumnName("error_count").IsRequired();
        builder.Property(version => version.WarningCount).HasColumnName("warning_count").IsRequired();
        builder.Property(version => version.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(version => version.ActivatedAt).HasColumnName("activated_at");
        builder.HasIndex(version => version.Status).HasDatabaseName("ix_player_dataset_versions_status");
    }
}

public sealed class FootballerConfiguration : IEntityTypeConfiguration<Footballer>
{
    public void Configure(EntityTypeBuilder<Footballer> builder)
    {
        builder.ToTable("footballers");
        builder.HasKey(footballer => footballer.Id);
        builder.Property(footballer => footballer.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(footballer => footballer.DatasetVersionId).HasColumnName("dataset_version_id").IsRequired();
        builder.Property(footballer => footballer.ExternalId).HasColumnName("external_id").IsRequired();
        builder.Property(footballer => footballer.CommonName).HasColumnName("common_name").HasMaxLength(128).IsRequired();
        builder.Property(footballer => footballer.FullName).HasColumnName("full_name").HasMaxLength(256);
        builder.Property(footballer => footballer.Overall).HasColumnName("overall").IsRequired();
        builder.Property(footballer => footballer.PrimaryPosition).HasColumnName("primary_position").HasMaxLength(8).IsRequired();
        builder.Property(footballer => footballer.Club).HasColumnName("club").HasMaxLength(128).IsRequired();
        builder.Property(footballer => footballer.League).HasColumnName("league").HasMaxLength(128).IsRequired();
        builder.Property(footballer => footballer.Nation).HasColumnName("nation").HasMaxLength(128).IsRequired();
        builder.Property(footballer => footballer.PreferredFoot).HasColumnName("preferred_foot").HasMaxLength(16);
        builder.Property(footballer => footballer.WeakFoot).HasColumnName("weak_foot").IsRequired();
        builder.Property(footballer => footballer.SkillMoves).HasColumnName("skill_moves").IsRequired();
        builder.Property(footballer => footballer.Height).HasColumnName("height").HasMaxLength(32);
        builder.Property(footballer => footballer.ImageUrl).HasColumnName("image_url").HasMaxLength(1024);
        builder.Property(footballer => footballer.SourceUrl).HasColumnName("source_url").HasMaxLength(1024);
        builder.Property(footballer => footballer.IsKickOffEligible).HasColumnName("is_kick_off_eligible").IsRequired();
        builder.Property(footballer => footballer.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(footballer => footballer.StatsJson).HasColumnName("stats_json").HasColumnType("jsonb").IsRequired();
        builder.Property(footballer => footballer.RolesJson).HasColumnName("roles_json").HasColumnType("jsonb").IsRequired();
        builder.Property(footballer => footballer.PlayStylesJson).HasColumnName("playstyles_json").HasColumnType("jsonb").IsRequired();

        builder.HasMany(footballer => footballer.Positions)
            .WithOne()
            .HasForeignKey(position => position.FootballerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PlayerDatasetVersion>()
            .WithMany()
            .HasForeignKey(footballer => footballer.DatasetVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(footballer => new { footballer.DatasetVersionId, footballer.ExternalId })
            .HasDatabaseName("ix_footballers_version_external_id")
            .IsUnique();
        builder.HasIndex(footballer => new { footballer.DatasetVersionId, footballer.Overall })
            .HasDatabaseName("ix_footballers_version_overall");
    }
}

public sealed class FootballerPositionConfiguration : IEntityTypeConfiguration<FootballerPosition>
{
    public void Configure(EntityTypeBuilder<FootballerPosition> builder)
    {
        builder.ToTable("footballer_positions");
        builder.HasKey(position => position.Id);
        builder.Property(position => position.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(position => position.FootballerId).HasColumnName("footballer_id").IsRequired();
        builder.Property(position => position.Position).HasColumnName("position").HasMaxLength(8).IsRequired();
        builder.Property(position => position.IsPrimary).HasColumnName("is_primary").IsRequired();
        builder.HasIndex(position => position.Position).HasDatabaseName("ix_footballer_positions_position");
    }
}

public sealed class ClubConfiguration : IEntityTypeConfiguration<Club>
{
    public void Configure(EntityTypeBuilder<Club> builder)
    {
        builder.ToTable("clubs");
        builder.HasKey(club => club.Id);
        builder.Property(club => club.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(club => club.DatasetVersionId).HasColumnName("dataset_version_id").IsRequired();
        builder.Property(club => club.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        builder.Property(club => club.League).HasColumnName("league").HasMaxLength(128).IsRequired();
        builder.Property(club => club.StarRating).HasColumnName("star_rating");
        builder.Property(club => club.IsFiveStarEligible).HasColumnName("is_five_star_eligible").IsRequired();

        builder.HasOne<PlayerDatasetVersion>()
            .WithMany()
            .HasForeignKey(club => club.DatasetVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(club => new { club.DatasetVersionId, club.Name })
            .HasDatabaseName("ix_clubs_version_name")
            .IsUnique();
    }
}

public sealed class DatasetImportIssueConfiguration : IEntityTypeConfiguration<DatasetImportIssue>
{
    public void Configure(EntityTypeBuilder<DatasetImportIssue> builder)
    {
        builder.ToTable("dataset_import_issues");
        builder.HasKey(issue => issue.Id);
        builder.Property(issue => issue.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(issue => issue.DatasetVersionId).HasColumnName("dataset_version_id").IsRequired();
        builder.Property(issue => issue.Severity).HasColumnName("severity").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(issue => issue.Row).HasColumnName("row").IsRequired();
        builder.Property(issue => issue.ExternalId).HasColumnName("external_id");
        builder.Property(issue => issue.Field).HasColumnName("field").HasMaxLength(64);
        builder.Property(issue => issue.Message).HasColumnName("message").HasMaxLength(512).IsRequired();

        builder.HasOne<PlayerDatasetVersion>()
            .WithMany()
            .HasForeignKey(issue => issue.DatasetVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(issue => issue.DatasetVersionId).HasDatabaseName("ix_dataset_import_issues_version");
    }
}
