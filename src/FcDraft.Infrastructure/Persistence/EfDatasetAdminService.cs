using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Datasets;
using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// Validates and persists dataset imports as draft versions, records per-row issues, and activates a
/// clean version while archiving the previously active one. Prior versions are retained (never
/// deleted) so a draft can pin the version it started with.
/// </summary>
public sealed class EfDatasetAdminService(FcDraftDbContext dbContext, IBundledDataset bundled) : IDatasetAdminService
{
    // Recognized FC 26 outfield/keeper positions. Rows with an unrecognized primary position are
    // rejected; unrecognized alternates are dropped with a warning.
    private static readonly HashSet<string> ValidPositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "GK", "RB", "RWB", "CB", "LB", "LWB", "CDM", "CM", "CAM", "RM", "LM", "RW", "LW", "CF", "ST",
    };

    private const int MinimumOverall = 75;

    public Task<DatasetImportReport> ImportBundledAsync(CancellationToken cancellationToken) =>
        ImportAsync(new DatasetImportRequest(bundled.Label, bundled.Source, bundled.Load()), cancellationToken);

    public async Task<DatasetImportReport> ImportAsync(DatasetImportRequest request, CancellationToken cancellationToken)
    {
        var version = new PlayerDatasetVersion
        {
            Label = string.IsNullOrWhiteSpace(request.Label) ? "Untitled" : request.Label.Trim(),
            Source = request.Source?.Trim() ?? string.Empty,
            Status = DatasetVersionStatus.Draft,
        };
        dbContext.PlayerDatasetVersions.Add(version);

        var issues = new List<DatasetImportIssue>();
        var seenExternalIds = new HashSet<int>();
        var clubs = new Dictionary<string, Club>(StringComparer.OrdinalIgnoreCase);
        var imported = 0;
        var rowNumber = 0;

        foreach (var row in request.Rows)
        {
            rowNumber++;

            if (row.ExternalId <= 0)
            {
                issues.Add(Error(version.Id, rowNumber, null, "ExternalId", "Missing or invalid external id."));
                continue;
            }

            if (!seenExternalIds.Add(row.ExternalId))
            {
                issues.Add(Error(version.Id, rowNumber, row.ExternalId, "ExternalId", "Duplicate external id in the import."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.CommonName))
            {
                issues.Add(Error(version.Id, rowNumber, row.ExternalId, "CommonName", "Missing player name."));
                continue;
            }

            var primary = row.Position?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(primary) || !ValidPositions.Contains(primary))
            {
                issues.Add(Error(version.Id, rowNumber, row.ExternalId, "Position",
                    $"Missing or invalid primary position '{row.Position}'."));
                continue;
            }

            var footballer = new Footballer
            {
                DatasetVersionId = version.Id,
                ExternalId = row.ExternalId,
                CommonName = row.CommonName!.Trim(),
                FullName = row.FullName?.Trim(),
                Overall = row.Overall,
                PrimaryPosition = primary,
                Club = row.Club?.Trim() ?? string.Empty,
                League = row.League?.Trim() ?? string.Empty,
                Nation = row.Nation?.Trim() ?? string.Empty,
                PreferredFoot = row.PreferredFoot?.Trim(),
                WeakFoot = row.WeakFoot,
                SkillMoves = row.SkillMoves,
                Height = row.Height?.Trim(),
                ImageUrl = row.ImageUrl?.Trim(),
                SourceUrl = row.SourceUrl?.Trim(),
                IsKickOffEligible = true,
                IsActive = row.Overall >= MinimumOverall,
                StatsJson = string.IsNullOrWhiteSpace(row.StatsJson) ? "[]" : row.StatsJson,
                RolesJson = string.IsNullOrWhiteSpace(row.RolesJson) ? "[]" : row.RolesJson,
                PlayStylesJson = string.IsNullOrWhiteSpace(row.PlayStylesJson) ? "[]" : row.PlayStylesJson,
            };

            footballer.Positions.Add(new FootballerPosition
            {
                FootballerId = footballer.Id,
                Position = primary,
                IsPrimary = true,
            });

            foreach (var alternate in row.AlternatePositions ?? [])
            {
                var alt = alternate?.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(alt) || alt == primary)
                {
                    continue;
                }

                if (!ValidPositions.Contains(alt))
                {
                    issues.Add(Warning(version.Id, rowNumber, row.ExternalId, "AlternatePositions",
                        $"Unrecognized alternate position '{alternate}' was dropped."));
                    continue;
                }

                if (footballer.Positions.All(position => position.Position != alt))
                {
                    footballer.Positions.Add(new FootballerPosition
                    {
                        FootballerId = footballer.Id,
                        Position = alt,
                        IsPrimary = false,
                    });
                }
            }

            if (row.Overall < MinimumOverall)
            {
                issues.Add(Warning(version.Id, rowNumber, row.ExternalId, "Overall",
                    $"Overall {row.Overall} is below the {MinimumOverall}+ draft eligibility; imported but inactive."));
            }

            if (string.IsNullOrWhiteSpace(row.Club))
            {
                issues.Add(Warning(version.Id, rowNumber, row.ExternalId, "Club", "Missing club."));
            }
            else if (!clubs.ContainsKey(footballer.Club))
            {
                var club = new Club
                {
                    DatasetVersionId = version.Id,
                    Name = footballer.Club,
                    League = footballer.League,
                };
                clubs[footballer.Club] = club;
                dbContext.Clubs.Add(club);
            }

            dbContext.Footballers.Add(footballer);
            imported++;
        }

        version.FootballerCount = imported;
        version.ClubCount = clubs.Count;
        version.ErrorCount = issues.Count(issue => issue.Severity == ImportIssueSeverity.Error);
        version.WarningCount = issues.Count(issue => issue.Severity == ImportIssueSeverity.Warning);
        dbContext.DatasetImportIssues.AddRange(issues);

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToReport(version, request.Rows.Count, issues);
    }

    public async Task<IReadOnlyList<DatasetVersionSummary>> ListVersionsAsync(CancellationToken cancellationToken) =>
        await dbContext.PlayerDatasetVersions
            .AsNoTracking()
            .OrderByDescending(version => version.CreatedAt)
            .Select(version => ToSummary(version))
            .ToArrayAsync(cancellationToken);

    public async Task<DatasetVersionDetail?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var version = await dbContext.PlayerDatasetVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == versionId, cancellationToken);
        if (version is null)
        {
            return null;
        }

        var issues = await dbContext.DatasetImportIssues
            .AsNoTracking()
            .Where(issue => issue.DatasetVersionId == versionId)
            .OrderBy(issue => issue.Row)
            .Take(500)
            .Select(issue => new DatasetImportIssueDto(
                issue.Severity.ToString(), issue.Row, issue.ExternalId, issue.Field, issue.Message))
            .ToArrayAsync(cancellationToken);

        return new DatasetVersionDetail(ToSummary(version), issues);
    }

    public async Task<DatasetVersionSummary> ActivateAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var version = await dbContext.PlayerDatasetVersions
            .FirstOrDefaultAsync(candidate => candidate.Id == versionId, cancellationToken)
            ?? throw new KeyNotFoundException("Dataset version not found.");

        if (version.Status == DatasetVersionStatus.Active)
        {
            return ToSummary(version);
        }

        if (version.ErrorCount > 0)
        {
            throw new ConflictAppException(
                $"Resolve the {version.ErrorCount} import error(s) before activating this version.");
        }

        var currentlyActive = await dbContext.PlayerDatasetVersions
            .Where(candidate => candidate.Status == DatasetVersionStatus.Active)
            .ToListAsync(cancellationToken);
        foreach (var active in currentlyActive)
        {
            active.Status = DatasetVersionStatus.Archived;
        }

        version.Status = DatasetVersionStatus.Active;
        version.ActivatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSummary(version);
    }

    private static DatasetImportReport ToReport(
        PlayerDatasetVersion version, int rowsTotal, IReadOnlyList<DatasetImportIssue> issues) =>
        new(
            version.Id,
            version.Label,
            version.Status.ToString(),
            rowsTotal,
            version.FootballerCount,
            version.ClubCount,
            version.ErrorCount,
            version.WarningCount,
            issues.Take(500)
                .Select(issue => new DatasetImportIssueDto(
                    issue.Severity.ToString(), issue.Row, issue.ExternalId, issue.Field, issue.Message))
                .ToArray());

    private static DatasetVersionSummary ToSummary(PlayerDatasetVersion version) => new(
        version.Id,
        version.Label,
        version.Source,
        version.Status.ToString(),
        version.FootballerCount,
        version.ClubCount,
        version.ErrorCount,
        version.WarningCount,
        version.CreatedAt,
        version.ActivatedAt);

    private static DatasetImportIssue Error(Guid versionId, int row, int? externalId, string field, string message) =>
        new()
        {
            DatasetVersionId = versionId,
            Severity = ImportIssueSeverity.Error,
            Row = row,
            ExternalId = externalId,
            Field = field,
            Message = message,
        };

    private static DatasetImportIssue Warning(Guid versionId, int row, int? externalId, string field, string message) =>
        new()
        {
            DatasetVersionId = versionId,
            Severity = ImportIssueSeverity.Warning,
            Row = row,
            ExternalId = externalId,
            Field = field,
            Message = message,
        };
}
