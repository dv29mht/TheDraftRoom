using FcDraft.Application.Features.Drafts;

namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// The draft eligibility read seam (PR-14/PR-15). Every read is scoped to an explicit dataset version — the
/// version a draft <b>pinned at start</b> (<c>Draft.PinnedDatasetVersionId</c>), never the currently-active
/// dataset — so a mid-draft dataset activation can never change an in-progress pool (PRD §6.3). It surfaces
/// only eligible content: curated five-star Kick Off clubs, and men's base/Kick Off footballers rated 75+.
/// Backed by the database when configured (<c>EfDraftCatalog</c>); the in-memory foundation serves the
/// bundled snapshot (<c>InMemoryDraftCatalog</c>).
/// </summary>
public interface IDraftCatalog
{
    /// <summary>The eligible five-star clubs in the given dataset version (empty when the version is unknown).</summary>
    Task<IReadOnlyList<CatalogClub>> ListFiveStarClubsAsync(Guid? datasetVersionId, CancellationToken cancellationToken);

    /// <summary>A specific five-star club, or null if it is not an eligible five-star club in this version.</summary>
    Task<CatalogClub?> FindFiveStarClubAsync(Guid? datasetVersionId, Guid clubId, CancellationToken cancellationToken);

    /// <summary>An eligible footballer by its dataset-stable external id, or null if not eligible in this version.</summary>
    Task<CatalogFootballer?> FindFootballerAsync(Guid? datasetVersionId, int footballerId, CancellationToken cancellationToken);

    /// <summary>
    /// The full §9.6 detail card (stats, roles, PlayStyles, league/nation) for an eligible footballer, or
    /// null if not eligible in this version. Read on demand when a card is opened — the list reads stay
    /// compact (PR-18).
    /// </summary>
    Task<CatalogFootballerCard?> FindFootballerCardAsync(Guid? datasetVersionId, int footballerId, CancellationToken cancellationToken);

    /// <summary>
    /// The eligible footballers matching <paramref name="filter"/> (by club and/or position, any position when
    /// the filter's position is null), ordered best-first. Availability (already held/drafted) is applied by
    /// the caller against the draft's picks.
    /// </summary>
    Task<IReadOnlyList<CatalogFootballer>> ListFootballersAsync(
        Guid? datasetVersionId, CatalogFootballerFilter filter, CancellationToken cancellationToken);
}
