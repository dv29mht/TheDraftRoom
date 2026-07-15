using FcDraft.Application.Features.Datasets;

namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Read side of the player dataset for the explorer and (later) draft pools. Serves only eligible
/// men's base/Kick Off footballers rated 75+ from the active dataset version; excluded and inactive
/// content never appears. Backed by the database when configured, or the bundled snapshot otherwise.
/// </summary>
public interface IPlayerQueryService
{
    Task<PlayerSearchResult> SearchAsync(PlayerQuery query, CancellationToken cancellationToken);
    Task<PlayerCardDto?> GetByExternalIdAsync(int externalId, CancellationToken cancellationToken);
    Task<PlayerFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken);
}
