using System.Security.Cryptography;
using System.Text;

namespace FcDraft.Infrastructure.Datasets;

/// <summary>
/// The curated default set of clubs eligible for the pre-draft club round: EA FC 26 men's clubs rated
/// <b>five stars or 4.5 stars</b> (DRAFT_RULES decision 3). The EA feed omits club star ratings, so this
/// list is transcribed from EA's official FC 26 men's club-rating reveal (7 five-star + 9 four-and-a-half-star
/// = 16 clubs) and the names match the bundled FC 26 dataset's exact spellings. It is the <b>default</b>:
/// the database seeds these as eligible at dataset import so a fresh deploy and the tests can run the club
/// round out of the box, and an admin can still curate the eligibility flag afterwards (e.g. add a 4-star
/// club, or drop one). The in-memory foundation (no admin curation) treats exactly these names as eligible.
/// (The type keeps its historical name for API stability; "five-star" here means the eligible 5★/4.5★ tier.)
/// </summary>
public static class FiveStarClubs
{
    // EA FC 26 five-star men's clubs (7), in the bundled dataset's spelling.
    private static readonly string[] FiveStar =
    [
        "Real Madrid",
        "FC Barcelona",
        "Paris SG",
        "Liverpool",
        "Manchester City",
        "Arsenal",
        "FC Bayern München",
    ];

    // EA FC 26 4.5-star men's clubs (9), in the bundled dataset's spelling. "Leverkusen" — NOT
    // "Bayer 04 Leverkusen" — is the dataset spelling; the old value silently excluded the club.
    private static readonly string[] FourAndHalfStar =
    [
        "Atlético de Madrid",
        "Newcastle Utd",
        "SSC Napoli",
        "Borussia Dortmund",
        "Spurs",
        "Chelsea",
        "Aston Villa",
        "Man Utd",
        "Leverkusen",
    ];

    public static readonly IReadOnlySet<string> Names =
        new HashSet<string>([.. FiveStar, .. FourAndHalfStar], StringComparer.OrdinalIgnoreCase);

    public static bool Contains(string? clubName) => clubName is not null && Names.Contains(clubName);
}

/// <summary>
/// The deterministic club id used across the in-memory foundation so a club has one stable identifier within a
/// process — <c>MD5(NAME)</c> as a Guid. Shared by the in-memory club directory (PR-09) and the in-memory
/// draft catalog (PR-14) so a footballer's mapped club id matches the club a team selects.
/// </summary>
public static class InMemoryClubId
{
    public static Guid For(string name) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes(name.ToUpperInvariant())));
}
