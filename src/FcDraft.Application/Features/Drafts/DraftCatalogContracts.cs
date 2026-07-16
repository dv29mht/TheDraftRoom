using System.Text.Json;

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
/// The full §9.6 detail card for one eligible footballer, scoped to the pinned dataset version like every
/// other catalog read (PR-18). Carries the display-only extras the compact <see cref="CatalogFootballer"/>
/// deliberately omits: league/nation, card stats, positional roles with <c>+</c>/<c>++</c> familiarity, and
/// PlayStyles — the JSON payloads pass through in the shape the dataset stored (like the explorer's
/// <c>PlayerCardDto</c>), so the room renders the same card the explorer does.
/// </summary>
public sealed record CatalogFootballerCard(
    int Id,
    string Name,
    string? FullName,
    int Overall,
    Guid ClubId,
    string ClubName,
    string League,
    string Nation,
    IReadOnlyList<string> Positions,
    JsonElement Stats,
    JsonElement Roles,
    JsonElement PlayStyles,
    string? ImageUrl);

/// <summary>
/// A filter for eligible-footballer reads: restrict to a <see cref="ClubId"/> (the held round) and/or a
/// <see cref="Position"/> the footballer must fill (a starter slot); a null position accepts any (a flexible
/// bench slot). <see cref="Search"/> narrows by name (case-insensitive substring) so the room's search
/// stays inside the pinned pool (PR-18). <see cref="Take"/> bounds the returned pool for the UI.
/// </summary>
public sealed record CatalogFootballerFilter(
    Guid? ClubId = null,
    string? Position = null,
    string? Search = null,
    int Take = 100);
