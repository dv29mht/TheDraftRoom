using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Datasets;

namespace FcDraft.Infrastructure.Datasets;

/// <summary>
/// Serves the player explorer from the bundled dataset when no database is configured. Applies the
/// same eligibility (75+), filtering, sorting, and paging as the database-backed service so the
/// explorer behaves identically in both configurations.
/// </summary>
public sealed class InMemoryPlayerQueryService(IBundledDataset bundled) : IPlayerQueryService
{
    private IEnumerable<FootballerImportRow> Eligible() =>
        bundled.Load().Where(row => row.Overall >= PlayerQuerySupport.MinimumOverall && row.ExternalId > 0);

    public Task<PlayerSearchResult> SearchAsync(PlayerQuery query, CancellationToken cancellationToken)
    {
        var pageSize = PlayerQuerySupport.NormalizePageSize(query.PageSize);
        var matches = Eligible().Where(row => MatchesFilters(row, query)).ToArray();

        var ordered = Sort(matches, query.Sort);
        var total = ordered.Length;
        var totalPages = PlayerQuerySupport.TotalPages(total, pageSize);
        var page = Math.Clamp(PlayerQuerySupport.NormalizePage(query.Page), 1, totalPages);

        IReadOnlyList<PlayerCardDto> items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToCard)
            .ToArray();

        return Task.FromResult(new PlayerSearchResult(items, page, pageSize, total, totalPages, bundled.Label));
    }

    public Task<PlayerCardDto?> GetByExternalIdAsync(int externalId, CancellationToken cancellationToken)
    {
        var row = Eligible().FirstOrDefault(candidate => candidate.ExternalId == externalId);
        return Task.FromResult(row is null ? null : ToCard(row));
    }

    public Task<PlayerFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken)
    {
        var eligible = Eligible().ToArray();
        var positions = eligible
            .SelectMany(row => new[] { row.Position }.Concat(row.AlternatePositions))
            .Where(position => !string.IsNullOrWhiteSpace(position))
            .Select(position => position!.ToUpperInvariant())
            .Distinct()
            .OrderBy(position => position)
            .ToArray();
        var leagues = eligible.Select(row => row.League ?? string.Empty).Where(league => league.Length > 0).Distinct().OrderBy(league => league).ToArray();
        var nations = eligible.Select(row => row.Nation ?? string.Empty).Where(nation => nation.Length > 0).Distinct().OrderBy(nation => nation).ToArray();
        return Task.FromResult(new PlayerFilterOptions(positions, leagues, nations));
    }

    private static bool MatchesFilters(FootballerImportRow row, PlayerQuery query)
    {
        if (query.MinOverall is { } min && row.Overall < min) return false;
        if (query.MaxOverall is { } max && row.Overall > max) return false;

        if (!string.IsNullOrWhiteSpace(query.Position))
        {
            var position = query.Position.Trim();
            var fills = string.Equals(row.Position, position, StringComparison.OrdinalIgnoreCase)
                || row.AlternatePositions.Any(alt => string.Equals(alt, position, StringComparison.OrdinalIgnoreCase));
            if (!fills) return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Club) && !string.Equals(row.Club, query.Club, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(query.League) && !string.Equals(row.League, query.League, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(query.Nation) && !string.Equals(row.Nation, query.Nation, StringComparison.OrdinalIgnoreCase)) return false;

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var inName = row.CommonName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false;
            var inClub = row.Club?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false;
            if (!inName && !inClub) return false;
        }

        return true;
    }

    private static FootballerImportRow[] Sort(FootballerImportRow[] rows, string? sort) => sort switch
    {
        "overall_asc" => rows.OrderBy(row => row.Overall).ThenBy(row => row.CommonName).ToArray(),
        "name_asc" => rows.OrderBy(row => row.CommonName).ThenByDescending(row => row.Overall).ToArray(),
        "name_desc" => rows.OrderByDescending(row => row.CommonName).ThenByDescending(row => row.Overall).ToArray(),
        _ => rows.OrderByDescending(row => row.Overall).ThenBy(row => row.CommonName).ToArray(),
    };

    private static PlayerCardDto ToCard(FootballerImportRow row) => new(
        row.ExternalId,
        row.CommonName ?? string.Empty,
        row.Overall,
        row.Position ?? string.Empty,
        row.AlternatePositions,
        row.Club ?? string.Empty,
        row.League ?? string.Empty,
        row.Nation ?? string.Empty,
        row.PreferredFoot,
        row.WeakFoot,
        row.SkillMoves,
        row.Height,
        PlayerQuerySupport.ParseJson(row.StatsJson),
        PlayerQuerySupport.ParseJson(row.PlayStylesJson),
        PlayerQuerySupport.ParseJson(row.RolesJson),
        row.ImageUrl,
        row.SourceUrl);
}
