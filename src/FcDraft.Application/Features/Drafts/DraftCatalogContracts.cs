namespace FcDraft.Application.Features.Drafts;

/// <summary>An eligible five-star club from a dataset version (id, name, league).</summary>
public sealed record CatalogClub(Guid Id, string Name, string League);

/// <summary>
/// An eligible footballer from a dataset version, carrying exactly what the pick engine validates and
/// displays: the dataset-stable external <see cref="Id"/> (EA id), rating, the club it maps to (id + name,
/// used for the held-player club-match rule), and the positions it fills (primary + alternates, uppercased).
/// </summary>
public sealed record CatalogFootballer(
    int Id,
    string Name,
    int Overall,
    Guid ClubId,
    string ClubName,
    IReadOnlyList<string> Positions);

/// <summary>
/// A filter for eligible-footballer reads: restrict to a <see cref="ClubId"/> (the held round) and/or a
/// <see cref="Position"/> the footballer must fill (a starter slot); a null position accepts any (a flexible
/// bench slot). <see cref="Take"/> bounds the returned pool for the UI.
/// </summary>
public sealed record CatalogFootballerFilter(
    Guid? ClubId = null,
    string? Position = null,
    string? Search = null,
    int Take = 100);
