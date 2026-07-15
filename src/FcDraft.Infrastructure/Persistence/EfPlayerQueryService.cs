using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Datasets;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Auth;
using FcDraft.Infrastructure.Datasets;
using Microsoft.EntityFrameworkCore;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// Serves the player explorer from the active dataset version. Filtering, sorting, and paging run in
/// the database. Only eligible rows (active, Kick Off, 75+) are ever returned, so excluded and
/// inactive content cannot leak into the pool.
/// </summary>
public sealed class EfPlayerQueryService(FcDraftDbContext dbContext) : IPlayerQueryService
{
    public async Task<PlayerSearchResult> SearchAsync(PlayerQuery query, CancellationToken cancellationToken)
    {
        var version = await ActiveVersionAsync(cancellationToken);
        var pageSize = PlayerQuerySupport.NormalizePageSize(query.PageSize);
        if (version is null)
        {
            return new PlayerSearchResult([], 1, pageSize, 0, 1, string.Empty);
        }

        var filtered = EligibleFootballers(version.Id);

        if (query.MinOverall is { } min)
        {
            filtered = filtered.Where(footballer => footballer.Overall >= min);
        }

        if (query.MaxOverall is { } max)
        {
            filtered = filtered.Where(footballer => footballer.Overall <= max);
        }

        if (!string.IsNullOrWhiteSpace(query.Position))
        {
            var position = query.Position.Trim().ToUpperInvariant();
            filtered = filtered.Where(footballer => footballer.Positions.Any(slot => slot.Position == position));
        }

        if (!string.IsNullOrWhiteSpace(query.Club))
        {
            filtered = filtered.Where(footballer => footballer.Club == query.Club);
        }

        if (!string.IsNullOrWhiteSpace(query.League))
        {
            filtered = filtered.Where(footballer => footballer.League == query.League);
        }

        if (!string.IsNullOrWhiteSpace(query.Nation))
        {
            filtered = filtered.Where(footballer => footballer.Nation == query.Nation);
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = $"%{UserDirectory.EscapeLike(search)}%";
            filtered = filtered.Where(footballer =>
                EF.Functions.ILike(footballer.CommonName, pattern, UserDirectory.LikeEscape)
                || EF.Functions.ILike(footballer.Club, pattern, UserDirectory.LikeEscape));
        }

        var total = await filtered.CountAsync(cancellationToken);
        var totalPages = PlayerQuerySupport.TotalPages(total, pageSize);
        var page = Math.Clamp(PlayerQuerySupport.NormalizePage(query.Page), 1, totalPages);

        var footballers = await ApplySort(filtered, query.Sort)
            .Include(footballer => footballer.Positions)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return new PlayerSearchResult(
            footballers.Select(ToCard).ToArray(), page, pageSize, total, totalPages, version.Label);
    }

    public async Task<PlayerCardDto?> GetByExternalIdAsync(int externalId, CancellationToken cancellationToken)
    {
        var version = await ActiveVersionAsync(cancellationToken);
        if (version is null)
        {
            return null;
        }

        var footballer = await EligibleFootballers(version.Id)
            .Include(candidate => candidate.Positions)
            .FirstOrDefaultAsync(candidate => candidate.ExternalId == externalId, cancellationToken);
        return footballer is null ? null : ToCard(footballer);
    }

    public async Task<PlayerFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken)
    {
        var version = await ActiveVersionAsync(cancellationToken);
        if (version is null)
        {
            return new PlayerFilterOptions([], [], []);
        }

        var eligible = EligibleFootballers(version.Id);

        var positions = await dbContext.FootballerPositions
            .Where(slot => eligible.Any(footballer => footballer.Id == slot.FootballerId))
            .Select(slot => slot.Position)
            .Distinct()
            .OrderBy(position => position)
            .ToArrayAsync(cancellationToken);

        var leagues = await eligible.Select(footballer => footballer.League).Distinct().OrderBy(league => league).ToArrayAsync(cancellationToken);
        var nations = await eligible.Select(footballer => footballer.Nation).Distinct().OrderBy(nation => nation).ToArrayAsync(cancellationToken);

        return new PlayerFilterOptions(positions, leagues, nations);
    }

    private async Task<PlayerDatasetVersion?> ActiveVersionAsync(CancellationToken cancellationToken) =>
        await dbContext.PlayerDatasetVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(version => version.Status == DatasetVersionStatus.Active, cancellationToken);

    private IQueryable<Footballer> EligibleFootballers(Guid versionId) =>
        dbContext.Footballers
            .AsNoTracking()
            .Where(footballer => footballer.DatasetVersionId == versionId
                && footballer.IsActive
                && footballer.IsKickOffEligible
                && footballer.Overall >= PlayerQuerySupport.MinimumOverall);

    private static IQueryable<Footballer> ApplySort(IQueryable<Footballer> query, string? sort) => sort switch
    {
        "overall_asc" => query.OrderBy(footballer => footballer.Overall).ThenBy(footballer => footballer.CommonName),
        "name_asc" => query.OrderBy(footballer => footballer.CommonName).ThenByDescending(footballer => footballer.Overall),
        "name_desc" => query.OrderByDescending(footballer => footballer.CommonName).ThenByDescending(footballer => footballer.Overall),
        _ => query.OrderByDescending(footballer => footballer.Overall).ThenBy(footballer => footballer.CommonName),
    };

    private static PlayerCardDto ToCard(Footballer footballer) => new(
        footballer.ExternalId,
        footballer.CommonName,
        footballer.Overall,
        footballer.PrimaryPosition,
        footballer.Positions.Where(position => !position.IsPrimary).Select(position => position.Position).ToArray(),
        footballer.Club,
        footballer.League,
        footballer.Nation,
        footballer.PreferredFoot,
        footballer.WeakFoot,
        footballer.SkillMoves,
        footballer.Height,
        PlayerQuerySupport.ParseJson(footballer.StatsJson),
        PlayerQuerySupport.ParseJson(footballer.PlayStylesJson),
        PlayerQuerySupport.ParseJson(footballer.RolesJson),
        footballer.ImageUrl,
        footballer.SourceUrl);
}
