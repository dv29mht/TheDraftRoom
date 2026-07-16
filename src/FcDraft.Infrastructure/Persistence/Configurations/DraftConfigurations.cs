using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FcDraft.Infrastructure.Persistence.Configurations;

/// <summary>
/// Explicit snake_case mappings for the persistent draft aggregate (PR-10): the draft and its
/// participants, teams, team members, snapshotted roster slots, and append-only events. <c>version</c>
/// is a concurrency token so a stale write loses (PRD §6.5), and the append-only event stream is kept
/// gap-free by a unique <c>(draft_id, sequence)</c> index.
/// </summary>
public sealed class DraftConfiguration : IEntityTypeConfiguration<Draft>
{
    public void Configure(EntityTypeBuilder<Draft> builder)
    {
        builder.ToTable("drafts");
        builder.HasKey(draft => draft.Id);
        builder.Property(draft => draft.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(draft => draft.Code).HasColumnName("code").HasMaxLength(16).IsRequired();
        builder.Property(draft => draft.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        builder.Property(draft => draft.Format).HasColumnName("format").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(draft => draft.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(draft => draft.HostUserId).HasColumnName("host_user_id").IsRequired();
        builder.Property(draft => draft.RosterTemplateId).HasColumnName("roster_template_id").IsRequired();
        builder.Property(draft => draft.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();
        builder.Property(draft => draft.PickTimerSeconds).HasColumnName("pick_timer_seconds").IsRequired();
        builder.Property(draft => draft.PinnedDatasetVersionId).HasColumnName("pinned_dataset_version_id");
        builder.Property(draft => draft.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(draft => draft.StartedAt).HasColumnName("started_at");
        builder.Property(draft => draft.CompletedAt).HasColumnName("completed_at");

        // The persisted pick-clock anchors (PR-16): the active position turn's start and, while paused, the
        // freeze instant. Nullable — set only during timed play — so the columns are forward-safe to add.
        builder.Property(draft => draft.TurnStartedAt).HasColumnName("turn_started_at");
        builder.Property(draft => draft.PausedAt).HasColumnName("paused_at");

        builder.HasMany(draft => draft.Participants).WithOne()
            .HasForeignKey(participant => participant.DraftId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(draft => draft.Teams).WithOne()
            .HasForeignKey(team => team.DraftId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(draft => draft.Slots).WithOne()
            .HasForeignKey(slot => slot.DraftId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(draft => draft.Picks).WithOne()
            .HasForeignKey(pick => pick.DraftId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(draft => draft.Events).WithOne()
            .HasForeignKey(evt => evt.DraftId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(draft => draft.Code).IsUnique().HasDatabaseName("ix_drafts_code");
        builder.HasIndex(draft => draft.HostUserId).HasDatabaseName("ix_drafts_host_user_id");
        builder.HasIndex(draft => draft.Status).HasDatabaseName("ix_drafts_status");
    }
}

public sealed class DraftParticipantConfiguration : IEntityTypeConfiguration<DraftParticipant>
{
    public void Configure(EntityTypeBuilder<DraftParticipant> builder)
    {
        builder.ToTable("draft_participants");
        builder.HasKey(participant => participant.Id);
        builder.Property(participant => participant.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(participant => participant.DraftId).HasColumnName("draft_id").IsRequired();
        builder.Property(participant => participant.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(participant => participant.IsHost).HasColumnName("is_host").IsRequired();
        builder.Property(participant => participant.Seed).HasColumnName("seed").HasConversion<string>().HasMaxLength(16);
        builder.Property(participant => participant.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(participant => participant.IsReady).HasColumnName("is_ready").IsRequired();
        builder.Property(participant => participant.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(participant => new { participant.DraftId, participant.UserId })
            .IsUnique().HasDatabaseName("ix_draft_participants_draft_user");
    }
}

public sealed class DraftTeamConfiguration : IEntityTypeConfiguration<DraftTeam>
{
    public void Configure(EntityTypeBuilder<DraftTeam> builder)
    {
        builder.ToTable("draft_teams");
        builder.HasKey(team => team.Id);
        builder.Property(team => team.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(team => team.DraftId).HasColumnName("draft_id").IsRequired();
        builder.Property(team => team.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        builder.Property(team => team.SpinnerRank).HasColumnName("spinner_rank");
        builder.Property(team => team.SelectedClubId).HasColumnName("selected_club_id");
        builder.Property(team => team.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasMany(team => team.Members).WithOne()
            .HasForeignKey(member => member.DraftTeamId).OnDelete(DeleteBehavior.Cascade);

        // Postgres treats NULLs as distinct, so many teams may sit at null rank before the spinner runs,
        // but once ranks are committed each rank is unique within the draft (PRD §11).
        builder.HasIndex(team => new { team.DraftId, team.SpinnerRank })
            .IsUnique().HasDatabaseName("ix_draft_teams_draft_rank");

        // The same NULL-distinct trick makes each chosen five-star club unique per lobby (PR-14, DRAFT_RULES
        // §5 club uniqueness) while leaving every team free to sit at null before it chooses.
        builder.HasIndex(team => new { team.DraftId, team.SelectedClubId })
            .IsUnique().HasDatabaseName("ix_draft_teams_draft_club");
    }
}

public sealed class DraftTeamMemberConfiguration : IEntityTypeConfiguration<DraftTeamMember>
{
    public void Configure(EntityTypeBuilder<DraftTeamMember> builder)
    {
        builder.ToTable("draft_team_members");
        builder.HasKey(member => member.Id);
        builder.Property(member => member.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(member => member.DraftId).HasColumnName("draft_id").IsRequired();
        builder.Property(member => member.DraftTeamId).HasColumnName("draft_team_id").IsRequired();
        builder.Property(member => member.ParticipantId).HasColumnName("participant_id").IsRequired();

        // A participant belongs to at most one team per draft.
        builder.HasIndex(member => new { member.DraftId, member.ParticipantId })
            .IsUnique().HasDatabaseName("ix_draft_team_members_draft_participant");
    }
}

public sealed class DraftRosterSlotConfiguration : IEntityTypeConfiguration<DraftRosterSlot>
{
    public void Configure(EntityTypeBuilder<DraftRosterSlot> builder)
    {
        builder.ToTable("draft_roster_slots");
        builder.HasKey(slot => slot.Id);
        builder.Property(slot => slot.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(slot => slot.DraftId).HasColumnName("draft_id").IsRequired();
        builder.Property(slot => slot.Order).HasColumnName("slot_order").IsRequired();
        builder.Property(slot => slot.SlotType).HasColumnName("slot_type").HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(slot => slot.Position).HasColumnName("position").HasMaxLength(8);
        builder.Property(slot => slot.Label).HasColumnName("label").HasMaxLength(64).IsRequired();

        builder.HasIndex(slot => new { slot.DraftId, slot.Order })
            .IsUnique().HasDatabaseName("ix_draft_roster_slots_draft_order");
    }
}

public sealed class DraftPickConfiguration : IEntityTypeConfiguration<DraftPick>
{
    public void Configure(EntityTypeBuilder<DraftPick> builder)
    {
        builder.ToTable("draft_picks");
        builder.HasKey(pick => pick.Id);
        builder.Property(pick => pick.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(pick => pick.DraftId).HasColumnName("draft_id").IsRequired();
        builder.Property(pick => pick.DraftTeamId).HasColumnName("draft_team_id").IsRequired();
        builder.Property(pick => pick.SlotOrder).HasColumnName("slot_order").IsRequired();
        builder.Property(pick => pick.FootballerId).HasColumnName("footballer_id").IsRequired();
        builder.Property(pick => pick.FootballerName).HasColumnName("footballer_name").HasMaxLength(128).IsRequired();
        builder.Property(pick => pick.FootballerOverall).HasColumnName("footballer_overall").IsRequired();
        builder.Property(pick => pick.FootballerPosition).HasColumnName("footballer_position").HasMaxLength(8);
        builder.Property(pick => pick.PickedByParticipantId).HasColumnName("picked_by_participant_id");
        builder.Property(pick => pick.CreatedAt).HasColumnName("created_at").IsRequired();

        // Global footballer uniqueness: once held or drafted, a footballer cannot be taken again (DRAFT_RULES
        // §5). A concurrent duplicate loses on this index and surfaces as a 409.
        builder.HasIndex(pick => new { pick.DraftId, pick.FootballerId })
            .IsUnique().HasDatabaseName("ix_draft_picks_draft_footballer");

        // Each team's slot fills exactly once (held slot 0 and every position slot), so the first valid
        // teammate submission for a slot wins and a stale/duplicate one is rejected transactionally.
        builder.HasIndex(pick => new { pick.DraftTeamId, pick.SlotOrder })
            .IsUnique().HasDatabaseName("ix_draft_picks_team_slot");
    }
}

public sealed class DraftEventConfiguration : IEntityTypeConfiguration<DraftEvent>
{
    public void Configure(EntityTypeBuilder<DraftEvent> builder)
    {
        builder.ToTable("draft_events");
        builder.HasKey(evt => evt.Id);
        builder.Property(evt => evt.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(evt => evt.DraftId).HasColumnName("draft_id").IsRequired();
        builder.Property(evt => evt.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(evt => evt.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(evt => evt.FromStatus).HasColumnName("from_status").HasConversion<string>().HasMaxLength(24);
        builder.Property(evt => evt.ToStatus).HasColumnName("to_status").HasConversion<string>().HasMaxLength(24);
        builder.Property(evt => evt.Version).HasColumnName("version").IsRequired();
        builder.Property(evt => evt.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(evt => evt.Reason).HasColumnName("reason").HasMaxLength(512);
        builder.Property(evt => evt.Payload).HasColumnName("payload");
        builder.Property(evt => evt.CreatedAt).HasColumnName("created_at").IsRequired();

        // The append-only history is kept gap-free and single-writer-per-step by this unique index.
        builder.HasIndex(evt => new { evt.DraftId, evt.Sequence })
            .IsUnique().HasDatabaseName("ix_draft_events_draft_sequence");
    }
}
