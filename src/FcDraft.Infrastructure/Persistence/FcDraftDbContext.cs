using System.Reflection;
using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// The application's PostgreSQL database context. Tables and columns are mapped explicitly to
/// snake_case through the configurations in <c>Persistence/Configurations</c>; the schema is
/// owned entirely by migrations so a clean database can be created without manual DDL.
/// </summary>
public sealed class FcDraftDbContext(DbContextOptions<FcDraftDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<PlatformMetadata> PlatformMetadata => Set<PlatformMetadata>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<SecurityAuditEvent> SecurityAuditEvents => Set<SecurityAuditEvent>();

    public DbSet<EmailOutboxMessage> EmailOutbox => Set<EmailOutboxMessage>();

    public DbSet<PlayerDatasetVersion> PlayerDatasetVersions => Set<PlayerDatasetVersion>();

    public DbSet<Footballer> Footballers => Set<Footballer>();

    public DbSet<FootballerPosition> FootballerPositions => Set<FootballerPosition>();

    public DbSet<Club> Clubs => Set<Club>();

    public DbSet<DatasetImportIssue> DatasetImportIssues => Set<DatasetImportIssue>();

    public DbSet<RosterTemplate> RosterTemplates => Set<RosterTemplate>();

    public DbSet<RosterSlot> RosterSlots => Set<RosterSlot>();

    public DbSet<Draft> Drafts => Set<Draft>();

    public DbSet<DraftParticipant> DraftParticipants => Set<DraftParticipant>();

    public DbSet<DraftTeam> DraftTeams => Set<DraftTeam>();

    public DbSet<DraftTeamMember> DraftTeamMembers => Set<DraftTeamMember>();

    public DbSet<DraftRosterSlot> DraftRosterSlots => Set<DraftRosterSlot>();

    public DbSet<DraftPick> DraftPicks => Set<DraftPick>();

    public DbSet<DraftEvent> DraftEvents => Set<DraftEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
