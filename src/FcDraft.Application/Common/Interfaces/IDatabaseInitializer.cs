namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Brings a database up to date at startup: applies any pending migrations so a clean database
/// is created exclusively from migrations, then seeds required platform metadata and (when
/// enabled) the deterministic development accounts. Registered only when SQL persistence is
/// configured; the in-memory foundation has nothing to initialize.
/// </summary>
public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
