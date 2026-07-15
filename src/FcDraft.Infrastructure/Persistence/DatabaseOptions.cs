namespace FcDraft.Infrastructure.Persistence;

/// <summary>Binds the <c>Database</c> configuration section that governs SQL persistence.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending migrations on startup so the schema is created from migrations alone.</summary>
    public bool MigrateOnStartup { get; set; } = true;

    /// <summary>
    /// Seed the deterministic development accounts (admin/player) when the directory is empty.
    /// Off by default so a production database never gains known-password accounts implicitly.
    /// </summary>
    public bool SeedDevelopmentAccounts { get; set; }

    /// <summary>
    /// Import and activate the bundled FC 26 dataset on a fresh database (only when no version yet
    /// exists), so the player explorer and draft pools work out of the box. On by default; set false
    /// to manage the dataset exclusively through admin imports.
    /// </summary>
    public bool SeedPlayerData { get; set; } = true;
}
