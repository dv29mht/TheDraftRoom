namespace FcDraft.Application.Features.Datasets;

/// <summary>One footballer row to import. Card stats, roles, and PlayStyles arrive as JSON text.</summary>
public sealed record FootballerImportRow(
    int ExternalId,
    string? CommonName,
    string? FullName,
    int Overall,
    string? Position,
    IReadOnlyList<string> AlternatePositions,
    string? Club,
    string? League,
    string? Nation,
    string? PreferredFoot,
    int WeakFoot,
    int SkillMoves,
    string? Height,
    string? ImageUrl,
    string? SourceUrl,
    string StatsJson,
    string RolesJson,
    string PlayStylesJson);

/// <summary>A request to import a dataset version. It is created as a draft, not activated.</summary>
public sealed record DatasetImportRequest(string Label, string Source, IReadOnlyList<FootballerImportRow> Rows);

public sealed record DatasetImportIssueDto(string Severity, int Row, int? ExternalId, string? Field, string Message);

public sealed record DatasetImportReport(
    Guid VersionId,
    string Label,
    string Status,
    int RowsTotal,
    int RowsImported,
    int ClubCount,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<DatasetImportIssueDto> Issues);

public sealed record DatasetVersionSummary(
    Guid Id,
    string Label,
    string Source,
    string Status,
    int FootballerCount,
    int ClubCount,
    int ErrorCount,
    int WarningCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ActivatedAt);

public sealed record DatasetVersionDetail(DatasetVersionSummary Summary, IReadOnlyList<DatasetImportIssueDto> Issues);
