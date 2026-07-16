using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// The database-backed draft eligibility read seam (PR-14/PR-15). Every read is scoped to the explicit pinned
/// dataset version, so an in-progress draft never follows a later dataset activation (PRD §6.3). Clubs are the
/// curated five-star Kick Off clubs of that version; footballers are men's base/Kick Off, rated 75+, active. A
/// footballer's club id is resolved from the version's <c>clubs</c> table by name (clubs are derived from the
/// same footballer feed at import, so the names match), giving the id the held-player club-match rule compares.
/// </summary>
public sealed class EfDraftCatalog(FcDraftDbContext dbContext) : IDraftCatalog
{
    private const int MinimumOverall = 75;

    public async Task<IReadOnlyList<CatalogClub>> ListFiveStarClubsAsync(Guid? datasetVersionId, CancellationToken cancellationToken)
    {
        if (datasetVersionId is not { } versionId)
        {
            return [];
        }

        return await dbContext.Clubs.AsNoTracking()
            .Where(club => club.DatasetVersionId == versionId && club.IsFiveStarEligible)
            .OrderBy(club => club.Name)
            .Select(club => new CatalogClub(club.Id, club.Name, club.League))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<CatalogClub?> FindFiveStarClubAsync(Guid? datasetVersionId, Guid clubId, CancellationToken cancellationToken)
    {
        if (datasetVersionId is not { } versionId)
        {
            return null;
        }

        return await dbContext.Clubs.AsNoTracking()
            .Where(club => club.Id == clubId && club.DatasetVersionId == versionId && club.IsFiveStarEligible)
            .Select(club => new CatalogClub(club.Id, club.Name, club.League))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CatalogFootballer?> FindFootballerAsync(Guid? datasetVersionId, int footballerId, CancellationToken cancellationToken)
    {
        if (datasetVersionId is not { } versionId)
        {
            return null;
        }

        var footballer = await EligibleFootballers(versionId)
            .Include(candidate => candidate.Positions)
            .FirstOrDefaultAsync(candidate => candidate.ExternalId == footballerId, cancellationToken);
        if (footballer is null)
        {
            return null;
        }

        var club = await dbContext.Clubs.AsNoTracking()
            .Where(candidate => candidate.DatasetVersionId == versionId && candidate.Name == footballer.Club)
            .Select(candidate => new { candidate.Id, candidate.Name })
            .FirstOrDefaultAsync(cancellationToken);
        return club is null ? null : ToCatalog(footballer, club.Id, club.Name);
    }

    public async Task<CatalogFootballerCard?> FindFootballerCardAsync(
        Guid? datasetVersionId, int footballerId, CancellationToken cancellationToken)
    {
        if (datasetVersionId is not { } versionId)
        {
            return null;
        }

        var footballer = await EligibleFootballers(versionId)
            .Include(candidate => candidate.Positions)
            .FirstOrDefaultAsync(candidate => candidate.ExternalId == footballerId, cancellationToken);
        if (footballer is null)
        {
            return null;
        }

        var clubId = await dbContext.Clubs.AsNoTracking()
            .Where(candidate => candidate.DatasetVersionId == versionId && candidate.Name == footballer.Club)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return clubId is null ? null : ToCard(footballer, clubId.Value);
    }

    public async Task<IReadOnlyDictionary<int, CatalogFootballerFacts>> MapFootballerFactsAsync(
        Guid? datasetVersionId, IReadOnlyCollection<int> footballerIds, CancellationToken cancellationToken)
    {
        if (datasetVersionId is not { } versionId || footballerIds.Count == 0)
        {
            return new Dictionary<int, CatalogFootballerFacts>();
        }

        var ids = footballerIds.ToArray();
        return await dbContext.Footballers.AsNoTracking()
            .Where(footballer => footballer.DatasetVersionId == versionId && ids.Contains(footballer.ExternalId))
            .Select(footballer => new CatalogFootballerFacts(footballer.ExternalId, footballer.Club, footballer.League, footballer.Nation))
            .ToDictionaryAsync(facts => facts.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogFootballer>> ListFootballersAsync(
        Guid? datasetVersionId, CatalogFootballerFilter filter, CancellationToken cancellationToken)
    {
        if (datasetVersionId is not { } versionId)
        {
            return [];
        }

        var query = EligibleFootballers(versionId).Include(footballer => footballer.Positions);

        if (filter.ClubId is { } clubId)
        {
            var clubName = await dbContext.Clubs.AsNoTracking()
                .Where(club => club.Id == clubId && club.DatasetVersionId == versionId)
                .Select(club => club.Name)
                .FirstOrDefaultAsync(cancellationToken);
            if (clubName is null)
            {
                return [];
            }
            query = query.Where(footballer => footballer.Club == clubName).Include(footballer => footballer.Positions);
        }
        if (!string.IsNullOrWhiteSpace(filter.Position))
        {
            var position = filter.Position.Trim().ToUpperInvariant();
            query = query.Where(footballer => footballer.Positions.Any(slot => slot.Position == position)).Include(footballer => footballer.Positions);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{UserDirectory.EscapeLike(filter.Search.Trim())}%";
            query = query.Where(footballer => EF.Functions.ILike(footballer.CommonName, pattern, UserDirectory.LikeEscape)).Include(footballer => footballer.Positions);
        }

        var footballers = await query
            .OrderByDescending(footballer => footballer.Overall)
            .ThenBy(footballer => footballer.CommonName)
            .ThenBy(footballer => footballer.ExternalId)
            .Take(Math.Clamp(filter.Take, 1, 500))
            .ToArrayAsync(cancellationToken);

        // Resolve each footballer's club id from the version's clubs (names match the derived club rows).
        var clubIdsByName = await dbContext.Clubs.AsNoTracking()
            .Where(club => club.DatasetVersionId == versionId)
            .Select(club => new { club.Id, club.Name })
            .ToDictionaryAsync(club => club.Name, club => club.Id, cancellationToken);

        return footballers
            .Where(footballer => clubIdsByName.ContainsKey(footballer.Club))
            .Select(footballer => ToCatalog(footballer, clubIdsByName[footballer.Club], footballer.Club))
            .ToArray();
    }

    private IQueryable<Footballer> EligibleFootballers(Guid versionId) =>
        dbContext.Footballers.AsNoTracking().Where(footballer =>
            footballer.DatasetVersionId == versionId
            && footballer.Overall >= MinimumOverall
            && footballer.IsKickOffEligible
            && footballer.IsActive);

    private static CatalogFootballer ToCatalog(Footballer footballer, Guid clubId, string clubName) => new(
        footballer.ExternalId,
        footballer.CommonName,
        footballer.Overall,
        clubId,
        clubName,
        OrderedPositions(footballer));

    private static CatalogFootballerCard ToCard(Footballer footballer, Guid clubId) => new(
        footballer.ExternalId,
        footballer.CommonName,
        footballer.FullName,
        footballer.Overall,
        clubId,
        footballer.Club,
        footballer.League,
        footballer.Nation,
        OrderedPositions(footballer),
        Datasets.PlayerQuerySupport.ParseJson(footballer.StatsJson),
        Datasets.PlayerQuerySupport.ParseJson(footballer.RolesJson),
        Datasets.PlayerQuerySupport.ParseJson(footballer.PlayStylesJson),
        footballer.ImageUrl);

    private static string[] OrderedPositions(Footballer footballer) =>
        footballer.Positions
            .OrderByDescending(position => position.IsPrimary)
            .Select(position => position.Position)
            .ToArray();
}
