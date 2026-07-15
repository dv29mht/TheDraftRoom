using FcDraft.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace FcDraft.Infrastructure.Auth;

/// <summary>
/// BCrypt password hasher (PRD §12.3). New hashes use BCrypt with a work factor of 12. Verification
/// also accepts the earlier ASP.NET Core Identity (PBKDF2) hash format so accounts seeded before
/// PR-05 keep signing in; those hashes upgrade to BCrypt the next time the password changes.
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    // Only used to verify legacy PBKDF2 hashes; the generic argument is irrelevant to verification.
    private static readonly PasswordHasher<object> LegacyHasher = new();

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return false;
        }

        // BCrypt hashes are self-describing ($2a$/$2b$/$2y$...); anything else is a legacy hash.
        if (hash.StartsWith("$2", StringComparison.Ordinal))
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return false;
            }
        }

        try
        {
            return LegacyHasher.VerifyHashedPassword(new object(), hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            // A stored value that is neither BCrypt nor a valid legacy hash cannot verify.
            return false;
        }
    }
}
