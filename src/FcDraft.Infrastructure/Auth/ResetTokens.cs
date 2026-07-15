using System.Security.Cryptography;

namespace FcDraft.Infrastructure.Auth;

/// <summary>
/// Generates and hashes password-reset tokens. The random token is emailed to the user; only its
/// SHA-256 hash is stored, so the stored value cannot be replayed to reset a password.
/// </summary>
internal static class ResetTokens
{
    /// <summary>How long a freshly issued reset token remains valid.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    /// <summary>A URL-safe, high-entropy token to email to the account.</summary>
    public static string Generate() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    /// <summary>Deterministic hash used for lookup and storage. Case-normalized to match Generate().</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token.Trim().ToLowerInvariant())));
}
