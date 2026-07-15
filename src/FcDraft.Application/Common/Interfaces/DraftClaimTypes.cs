namespace FcDraft.Application.Common.Interfaces;

/// <summary>Custom JWT claim names shared by the token issuer and the request-time validators.</summary>
public static class DraftClaimTypes
{
    /// <summary>Present and "true" while the account must still set a permanent password.</summary>
    public const string PasswordChangeRequired = "pwd_change_required";

    /// <summary>The account's security stamp; re-checked on every request so rotation revokes tokens.</summary>
    public const string SecurityStamp = "sstamp";
}
