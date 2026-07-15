using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations (so the schema is created exclusively from migrations) and seeds
/// required platform metadata plus, when enabled, the deterministic development accounts. Every
/// step is idempotent, so running it on each startup is safe.
/// </summary>
public sealed class DatabaseInitializer(
    FcDraftDbContext dbContext,
    IPasswordHasher hasher,
    IDatasetAdminService datasets,
    IOptions<DatabaseOptions> options)
    : IDatabaseInitializer
{
    private readonly DatabaseOptions _options = options.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_options.MigrateOnStartup)
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        await SeedPlatformMetadataAsync(cancellationToken);

        if (_options.SeedDevelopmentAccounts)
        {
            await SeedDevelopmentAccountsAsync(cancellationToken);
        }

        await SeedDefaultRosterTemplateAsync(cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        if (_options.SeedPlayerData)
        {
            await SeedPlayerDatasetAsync(cancellationToken);
        }
    }

    private async Task SeedDefaultRosterTemplateAsync(CancellationToken cancellationToken)
    {
        // The app needs an active roster template to run a draft; seed the locked 4-3-3 once.
        if (await dbContext.RosterTemplates.AnyAsync(cancellationToken))
        {
            return;
        }

        var template = new RosterTemplate
        {
            Name = Rosters.DefaultRosterTemplate.TemplateName,
            PickTimerSeconds = Rosters.DefaultRosterTemplate.PickTimerSeconds,
            IsActive = true,
        };
        foreach (var slot in Rosters.DefaultRosterTemplate.Slots())
        {
            template.Slots.Add(new RosterSlot
            {
                TemplateId = template.Id,
                Order = slot.Order,
                SlotType = slot.SlotType,
                Position = slot.Position,
                Label = slot.Label,
            });
        }

        dbContext.RosterTemplates.Add(template);
    }

    private async Task SeedPlayerDatasetAsync(CancellationToken cancellationToken)
    {
        // Only on a truly fresh database: importing the whole dataset is expensive and versions are
        // retained, so never re-import when any version already exists.
        if (await dbContext.PlayerDatasetVersions.AnyAsync(cancellationToken))
        {
            return;
        }

        var report = await datasets.ImportBundledAsync(cancellationToken);
        if (report.ErrorCount == 0)
        {
            await datasets.ActivateAsync(report.VersionId, cancellationToken);
        }
    }

    private async Task SeedPlatformMetadataAsync(CancellationToken cancellationToken)
    {
        const string platformNameKey = "platform.name";
        var exists = await dbContext.PlatformMetadata
            .AnyAsync(metadata => metadata.Key == platformNameKey, cancellationToken);
        if (!exists)
        {
            dbContext.PlatformMetadata.Add(new PlatformMetadata
            {
                Key = platformNameKey,
                Value = "The Draft Room",
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
    }

    private async Task SeedDevelopmentAccountsAsync(CancellationToken cancellationToken)
    {
        // mdevansh@gmail.com is the single designated administrator account (see PRD §9.2).
        await EnsureAccountAsync(
            "mdevansh@gmail.com", "Draft Room Admin", UserRole.Admin, "DraftAdmin@2026", cancellationToken);
        await EnsureAccountAsync(
            "player@draftroom.dev", "Practice Player", UserRole.Player, "Player@2026", cancellationToken);
    }

    private async Task EnsureAccountAsync(
        string email,
        string displayName,
        UserRole role,
        string password,
        CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToUpperInvariant();
        var exists = await dbContext.Users
            .AnyAsync(user => user.EmailNormalized == normalized, cancellationToken);
        if (exists)
        {
            return;
        }

        var user = new User
        {
            DisplayName = displayName,
            Email = email,
            EmailNormalized = normalized,
            PasswordHash = string.Empty,
            Role = role,
            Status = AccountStatus.Active,
            MustChangePassword = false,
        };
        user.PasswordHash = hasher.Hash(password);
        dbContext.Users.Add(user);
    }
}
