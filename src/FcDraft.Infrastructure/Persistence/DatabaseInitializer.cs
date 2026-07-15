using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using Microsoft.AspNetCore.Identity;
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
    IOptions<DatabaseOptions> options)
    : IDatabaseInitializer
{
    private static readonly PasswordHasher<User> Hasher = new();

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

        await dbContext.SaveChangesAsync(cancellationToken);
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
        await EnsureAccountAsync(
            "admin@draftroom.dev", "Draft Room Admin", UserRole.Admin, "DraftAdmin@2026", cancellationToken);
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
        user.PasswordHash = Hasher.HashPassword(user, password);
        dbContext.Users.Add(user);
    }
}
