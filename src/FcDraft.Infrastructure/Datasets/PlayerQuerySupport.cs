using System.Text.Json;

namespace FcDraft.Infrastructure.Datasets;

/// <summary>Shared helpers so the EF and in-memory player-query services page and shape identically.</summary>
internal static class PlayerQuerySupport
{
    public const int DefaultPageSize = 48;
    public const int MaxPageSize = 96;
    public const int MinimumOverall = 75;

    private static readonly JsonElement EmptyArray = JsonDocument.Parse("[]").RootElement.Clone();

    public static int NormalizePageSize(int pageSize) =>
        pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

    public static int NormalizePage(int page) => page < 1 ? 1 : page;

    public static int TotalPages(int total, int pageSize) => Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

    /// <summary>Parses stored JSON text into an element for passthrough; empty array on any problem.</summary>
    public static JsonElement ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return EmptyArray;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return EmptyArray;
        }
    }
}
