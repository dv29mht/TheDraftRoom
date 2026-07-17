using FcDraft.Infrastructure.Datasets;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// The default eligible-club set for the pre-draft club round (PR-25): EA FC 26 men's 5★ + 4.5★
/// Kick Off clubs, transcribed to the bundled dataset's exact spellings so every intended club is
/// actually offered. Guards the Leverkusen spelling fix and the 5★/4.5★-only scope.
/// </summary>
public sealed class EligibleClubsTests
{
    [Fact]
    public void Contains_all_16_men_five_and_four_and_half_star_clubs_in_dataset_spelling()
    {
        string[] expected =
        [
            // 5★
            "Real Madrid", "FC Barcelona", "Paris SG", "Liverpool", "Manchester City", "Arsenal", "FC Bayern München",
            // 4.5★
            "Atlético de Madrid", "Newcastle Utd", "SSC Napoli", "Borussia Dortmund", "Spurs", "Chelsea",
            "Aston Villa", "Man Utd", "Leverkusen",
        ];

        Assert.Equal(16, FiveStarClubs.Names.Count);
        foreach (var club in expected)
        {
            Assert.True(FiveStarClubs.Contains(club), $"{club} should be eligible");
        }
    }

    [Fact]
    public void Leverkusen_uses_the_dataset_spelling_not_the_old_full_name()
    {
        // The old seed spelling silently excluded the club (no dataset row matched it).
        Assert.True(FiveStarClubs.Contains("Leverkusen"));
        Assert.False(FiveStarClubs.Contains("Bayer 04 Leverkusen"));
    }

    [Fact]
    public void Sub_tier_clubs_are_not_in_the_default_set()
    {
        // 4-star (or lower) men's clubs are no longer defaulted eligible; an admin can still curate them.
        foreach (var club in new[] { "Juventus", "Inter", "AC Milan", "OM" })
        {
            Assert.False(FiveStarClubs.Contains(club), $"{club} should not be a default eligible club");
        }
    }

    [Fact]
    public void Membership_is_case_insensitive()
    {
        Assert.True(FiveStarClubs.Contains("real madrid"));
        Assert.False(FiveStarClubs.Contains("Some Sunday League FC"));
    }
}
