using System.Text.Json;
using FcDraft.Application.Features.Datasets;

namespace FcDraft.Infrastructure.Datasets;

/// <summary>
/// Parses the FC 26 dataset JSON shape (<c>{ version, source, players:[…] }</c>) into import rows.
/// Shared by the bundled-resource loader and the admin upload endpoint so both accept the same format.
/// </summary>
public static class DatasetJsonParser
{
    public static (string Label, string Source, IReadOnlyList<FootballerImportRow> Rows) Parse(JsonElement root)
    {
        var label = GetString(root, "version") ?? "FC26";
        var source = GetString(root, "source") ?? string.Empty;
        var rows = root.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Array
            ? ParsePlayers(players)
            : [];
        return (label, source, rows);
    }

    public static IReadOnlyList<FootballerImportRow> ParsePlayers(JsonElement playersArray)
    {
        var rows = new List<FootballerImportRow>();
        foreach (var player in playersArray.EnumerateArray())
        {
            rows.Add(new FootballerImportRow(
                ExternalId: GetInt(player, "id"),
                CommonName: GetString(player, "name"),
                FullName: GetString(player, "fullName"),
                Overall: GetInt(player, "overall"),
                Position: GetString(player, "position"),
                AlternatePositions: GetStringArray(player, "alternatePositions"),
                Club: GetString(player, "club"),
                League: GetString(player, "league"),
                Nation: GetString(player, "nation"),
                PreferredFoot: GetString(player, "preferredFoot"),
                WeakFoot: GetInt(player, "weakFoot"),
                SkillMoves: GetInt(player, "skillMoves"),
                Height: GetString(player, "height"),
                ImageUrl: GetString(player, "imageUrl"),
                SourceUrl: GetString(player, "sourceUrl"),
                StatsJson: GetRaw(player, "stats"),
                RolesJson: GetRaw(player, "roles"),
                PlayStylesJson: GetRaw(player, "playstyles")));
        }

        return rows;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();
    }

    private static string GetRaw(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.GetRawText() : "[]";
}
