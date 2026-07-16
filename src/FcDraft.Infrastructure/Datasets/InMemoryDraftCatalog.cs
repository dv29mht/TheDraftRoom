using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Datasets;
using FcDraft.Application.Features.Drafts;

namespace FcDraft.Infrastructure.Datasets;

/// <summary>
/// The draft eligibility read seam for the in-memory foundation (and the hermetic integration tests). Serves
/// the bundled snapshot: the curated <see cref="FiveStarClubs"/> as eligible clubs and men's base/Kick Off
/// footballers rated 75+ as the pool. The in-memory foundation has exactly one dataset, so the pinned version
/// id is not used to select data (there is nothing else to select); the database-backed
/// <c>EfDraftCatalog</c> scopes strictly by the pinned version. Club ids match
/// <see cref="InMemoryClubId"/> so a protected/drafted footballer maps to the club a team selects.
/// </summary>
public sealed class InMemoryDraftCatalog(IBundledDataset bundled) : IDraftCatalog
{
    private IEnumerable<FootballerImportRow> Eligible() =>
        bundled.Load().Where(row =>
            row.Overall >= PlayerQuerySupport.MinimumOverall
            && row.ExternalId > 0
            && !string.IsNullOrWhiteSpace(row.Club));

    public Task<IReadOnlyList<CatalogClub>> ListFiveStarClubsAsync(Guid? datasetVersionId, CancellationToken cancellationToken)
    {
        IReadOnlyList<CatalogClub> clubs = Eligible()
            .Where(row => FiveStarClubs.Contains(row.Club))
            .GroupBy(row => row.Club!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CatalogClub(
                InMemoryClubId.For(group.Key),
                group.Key,
                group.Select(row => row.League ?? string.Empty).FirstOrDefault(league => league.Length > 0) ?? string.Empty))
            .OrderBy(club => club.Name)
            .ToArray();
        return Task.FromResult(clubs);
    }

    public Task<CatalogClub?> FindFiveStarClubAsync(Guid? datasetVersionId, Guid clubId, CancellationToken cancellationToken)
    {
        var row = Eligible()
            .Where(candidate => FiveStarClubs.Contains(candidate.Club))
            .FirstOrDefault(candidate => InMemoryClubId.For(candidate.Club!) == clubId);
        return Task.FromResult(row is null
            ? null
            : new CatalogClub(clubId, row.Club!, row.League ?? string.Empty));
    }

    public Task<CatalogFootballer?> FindFootballerAsync(Guid? datasetVersionId, int footballerId, CancellationToken cancellationToken)
    {
        var row = Eligible().FirstOrDefault(candidate => candidate.ExternalId == footballerId);
        return Task.FromResult(row is null ? null : ToCatalog(row));
    }

    public Task<CatalogFootballerCard?> FindFootballerCardAsync(Guid? datasetVersionId, int footballerId, CancellationToken cancellationToken)
    {
        var row = Eligible().FirstOrDefault(candidate => candidate.ExternalId == footballerId);
        return Task.FromResult(row is null
            ? null
            : new CatalogFootballerCard(
                row.ExternalId,
                row.CommonName ?? string.Empty,
                row.FullName,
                row.Overall,
                InMemoryClubId.For(row.Club!),
                row.Club!,
                row.League ?? string.Empty,
                row.Nation ?? string.Empty,
                Positions(row),
                PlayerQuerySupport.ParseJson(row.StatsJson),
                PlayerQuerySupport.ParseJson(row.RolesJson),
                PlayerQuerySupport.ParseJson(row.PlayStylesJson),
                row.ImageUrl));
    }

    public Task<IReadOnlyList<CatalogFootballer>> ListFootballersAsync(
        Guid? datasetVersionId, CatalogFootballerFilter filter, CancellationToken cancellationToken)
    {
        var query = Eligible();
        if (filter.ClubId is { } clubId)
        {
            query = query.Where(row => InMemoryClubId.For(row.Club!) == clubId);
        }
        if (!string.IsNullOrWhiteSpace(filter.Position))
        {
            query = query.Where(row => FillsPosition(row, filter.Position));
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(row => (row.CommonName ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        IReadOnlyList<CatalogFootballer> results = query
            .OrderByDescending(row => row.Overall)
            .ThenBy(row => row.CommonName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ExternalId)
            .Take(Math.Clamp(filter.Take, 1, 500))
            .Select(ToCatalog)
            .ToArray();
        return Task.FromResult(results);
    }

    private static bool FillsPosition(FootballerImportRow row, string position) =>
        string.Equals(row.Position, position, StringComparison.OrdinalIgnoreCase)
        || row.AlternatePositions.Any(alt => string.Equals(alt, position, StringComparison.OrdinalIgnoreCase));

    private static CatalogFootballer ToCatalog(FootballerImportRow row) => new(
        row.ExternalId,
        row.CommonName ?? string.Empty,
        row.Overall,
        InMemoryClubId.For(row.Club!),
        row.Club!,
        Positions(row));

    private static IReadOnlyList<string> Positions(FootballerImportRow row) =>
        new[] { row.Position }
            .Concat(row.AlternatePositions)
            .Where(position => !string.IsNullOrWhiteSpace(position))
            .Select(position => position!.ToUpperInvariant())
            .Distinct()
            .ToArray();
}
