using System.Text.Json;
using FcDraft.Infrastructure.Datasets;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// The bundled FC 26 dataset carries positional role familiarity (Role / Role+ / Role++), backfilled
/// from WeFUT for base-card matches (see fc-draft-web/scripts/apply-wefut-roles.mjs and
/// WEFUT_ROLES_IMPORT.md). EA's public ratings feed omits roles, so before the backfill every row
/// shipped <c>roles: []</c>. These tests load the real embedded dataset through the real parser and
/// assert the roles land in the display shape the UI renders: <c>{ position, name, familiarity }</c>.
/// </summary>
public sealed class DatasetRolesTests
{
    private sealed record Role(string Position, string Name, int Familiarity);

    private static readonly IReadOnlyList<(int ExternalId, IReadOnlyList<Role> Roles)> Fixture =
        BundledPlayerDataset()
            .Select(row => (row.ExternalId, (IReadOnlyList<Role>)ParseRoles(row.RolesJson)))
            .ToList();

    private static IReadOnlyList<Application.Features.Datasets.FootballerImportRow> BundledPlayerDataset() =>
        new BundledPlayerDataset().Load();

    private static List<Role> ParseRoles(string rolesJson) =>
        JsonSerializer.Deserialize<List<Role>>(rolesJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

    private static IReadOnlyList<Role> RolesFor(int externalId) =>
        Fixture.Single(f => f.ExternalId == externalId).Roles;

    [Fact]
    public void Bundled_dataset_populates_roles_for_a_substantial_share_of_players()
    {
        var withRoles = Fixture.Count(f => f.Roles.Count > 0);
        // 1716 of 1748 at import time; assert a healthy floor so the backfill can't silently regress to empty.
        Assert.True(withRoles >= 1500, $"expected most players to carry roles, but only {withRoles} do");
    }

    [Fact]
    public void Every_role_entry_is_well_shaped()
    {
        foreach (var (externalId, roles) in Fixture)
        {
            foreach (var role in roles)
            {
                Assert.False(string.IsNullOrWhiteSpace(role.Position), $"{externalId} role has empty position");
                Assert.False(string.IsNullOrWhiteSpace(role.Name), $"{externalId} role has empty name");
                Assert.InRange(role.Familiarity, 0, 2);
            }
        }
    }

    [Fact]
    public void Known_striker_has_his_signature_role_at_the_plus_plus_tier()
    {
        // Erling Haaland (EA id 239085): base card is Advanced Forward++ (familiarity 2 => "++").
        var roles = RolesFor(239085);
        Assert.Contains(roles, r => r is { Position: "ST", Name: "Advanced Forward", Familiarity: 2 });
    }

    [Fact]
    public void Base_card_roles_at_different_tiers_both_survive()
    {
        // Virgil van Dijk (EA id 203376): base card is Defender+ AND Ball-Playing Defender++. Regression
        // guard for the crawler dedup fix — a same-OVR promo (globetrotters Defender++) must not clobber
        // the base card's Defender+ row.
        var roles = RolesFor(203376);
        Assert.Contains(roles, r => r is { Position: "CB", Name: "Defender", Familiarity: 1 });
        Assert.Contains(roles, r => r is { Position: "CB", Name: "Ball-Playing Defender", Familiarity: 2 });
    }
}
