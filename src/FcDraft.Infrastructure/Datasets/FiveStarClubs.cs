using System.Security.Cryptography;
using System.Text;

namespace FcDraft.Infrastructure.Datasets;

/// <summary>
/// The curated default set of eligible five-star Kick Off clubs (the EA feed omits club star ratings, so
/// eligibility is curated — DRAFT_RULES decision 3, PR-09). This is the <b>default</b>: the database seeds
/// these as five-star at dataset import so a fresh deploy and the tests can run the club round out of the box,
/// and an admin can still curate the flag afterwards. The in-memory foundation (no admin curation) treats
/// exactly these club names as five-star. Names match the bundled FC 26 dataset.
/// </summary>
public static class FiveStarClubs
{
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Real Madrid",
        "FC Barcelona",
        "Atlético de Madrid",
        "Manchester City",
        "Liverpool",
        "Arsenal",
        "Chelsea",
        "Man Utd",
        "Spurs",
        "Newcastle Utd",
        "Aston Villa",
        "FC Bayern München",
        "Bayer 04 Leverkusen",
        "Borussia Dortmund",
        "Paris SG",
        "OM",
        "Juventus",
        "Inter",
        "AC Milan",
        "SSC Napoli",
    };

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
