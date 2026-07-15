namespace FcDraft.Infrastructure.Auth;

/// <summary>
/// Shared directory helpers used by both identity stores so paging, search escaping, and profile
/// normalization behave identically whether the backing store is PostgreSQL or the in-memory
/// foundation.
/// </summary>
internal static class UserDirectory
{
    /// <summary>Escape character passed to PostgreSQL <c>ILIKE</c> for literal wildcard matching.</summary>
    public const string LikeEscape = "\\";

    private static readonly int[] AllowedPageSizes = [10, 25, 50];

    /// <summary>Clamps an arbitrary page size to the supported directory view sizes.</summary>
    public static int NormalizePageSize(int pageSize) =>
        Array.IndexOf(AllowedPageSizes, pageSize) >= 0 ? pageSize : AllowedPageSizes[0];

    /// <summary>At least one page always exists so the UI never reports "Page 1 of 0".</summary>
    public static int TotalPages(int total, int pageSize) =>
        Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

    /// <summary>Escapes <c>%</c>, <c>_</c>, and the escape char itself for a safe ILIKE pattern.</summary>
    public static string EscapeLike(string term) => term
        .Replace(LikeEscape, LikeEscape + LikeEscape)
        .Replace("%", LikeEscape + "%")
        .Replace("_", LikeEscape + "_");

    /// <summary>Treats a blank optional field as absent so empty inputs clear the value.</summary>
    public static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
