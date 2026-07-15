namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Rate-limits repeated failed sign-ins and applies a temporary lockout, protecting accounts (and
/// the shared, known temporary credentials) from brute force. State is per-process and keyed by
/// normalized email; the login handler is the only caller.
/// </summary>
public interface ILoginThrottle
{
    /// <summary>Current lockout state for an email; the handler rejects the attempt when locked.</summary>
    LockoutState Check(string email);

    /// <summary>Records a failed attempt and returns the resulting state (locked once the cap is hit).</summary>
    LockoutState RegisterFailure(string email);

    /// <summary>Clears the failure counter after a successful sign-in.</summary>
    void Reset(string email);
}

/// <summary>Whether an account is currently locked out and, if so, for how much longer.</summary>
public sealed record LockoutState(bool IsLockedOut, TimeSpan RetryAfter)
{
    public static readonly LockoutState Unlocked = new(false, TimeSpan.Zero);
}
