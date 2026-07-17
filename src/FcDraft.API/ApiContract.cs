namespace FcDraft.API;

/// <summary>
/// The client/API compatibility contract (PR-22, PRD §12.2 and the §18 "cached PWA shell becomes
/// incompatible with API" risk). The SPA compiles the same number into its bundle
/// (fc-draft-web/src/services/apiContract.ts — a frontend test asserts the two stay equal) and
/// compares it against the <see cref="HeaderName"/> header stamped on every /api response: a
/// mismatch means the service worker is holding a stale shell, and the client prompts for the
/// waiting update. Bump the number ONLY on a breaking API change — it forces every installed
/// client to refresh.
/// </summary>
public static class ApiContract
{
    public const int Version = 1;

    /// <summary>Response header carrying <see cref="Version"/> on every /api response.</summary>
    public const string HeaderName = "X-DraftRoom-Contract";
}
