using System.Text.Json;

namespace FcDraft.Application.Features.Datasets;

/// <summary>Search/filter/sort/paging inputs for the player explorer. Normalization is in the service.</summary>
public sealed record PlayerQuery(
    string? Search,
    string? Position,
    int? MinOverall,
    int? MaxOverall,
    string? Club,
    string? League,
    string? Nation,
    string? Sort,
    int Page,
    int PageSize);

/// <summary>
/// A footballer card for the explorer. Stats/PlayStyles/roles are passed through as JSON so the
/// client renders the same shape the dataset stored, without a bespoke DTO per attribute type.
/// </summary>
public sealed record PlayerCardDto(
    int Id,
    string Name,
    int Overall,
    string Position,
    IReadOnlyList<string> AlternatePositions,
    string Club,
    string League,
    string Nation,
    string? PreferredFoot,
    int WeakFoot,
    int SkillMoves,
    string? Height,
    JsonElement Stats,
    JsonElement Playstyles,
    JsonElement Roles,
    string? ImageUrl,
    string? SourceUrl);

public sealed record PlayerSearchResult(
    IReadOnlyList<PlayerCardDto> Items,
    int Page,
    int PageSize,
    int Total,
    int TotalPages,
    string DatasetLabel);

/// <summary>Distinct filter values from the active dataset, for the explorer's filter controls.</summary>
public sealed record PlayerFilterOptions(
    IReadOnlyList<string> Positions,
    IReadOnlyList<string> Leagues,
    IReadOnlyList<string> Nations);
