namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Hashes and verifies account passwords. The MVP implementation is BCrypt (PRD §12.3). Verification
/// also accepts pre-existing hashes produced by the earlier ASP.NET Core Identity hasher so a
/// database seeded before PR-05 keeps working; such hashes upgrade to BCrypt on the next change.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}
