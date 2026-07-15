using System.Collections.Concurrent;
using FcDraft.Application.Common.Interfaces;

namespace FcDraft.Infrastructure.Auth;

/// <summary>
/// In-memory failed-login throttle. After <see cref="MaxAttempts"/> failures within the rolling
/// <see cref="Window"/>, the account is locked for <see cref="Lockout"/>. Time comes from an
/// injected <see cref="TimeProvider"/> so lockout expiry is deterministic in tests. State is
/// per-process, which is sufficient for the single-instance MVP deployment.
/// </summary>
public sealed class LoginThrottle(TimeProvider clock) : ILoginThrottle
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);

    private sealed record Attempts(int Count, DateTimeOffset FirstFailureAt, DateTimeOffset? LockedUntil);

    private readonly ConcurrentDictionary<string, Attempts> _byEmail = new(StringComparer.OrdinalIgnoreCase);

    public LockoutState Check(string email)
    {
        var key = Normalize(email);
        if (!_byEmail.TryGetValue(key, out var attempts))
        {
            return LockoutState.Unlocked;
        }

        var now = clock.GetUtcNow();
        if (attempts.LockedUntil is { } until && now < until)
        {
            return new LockoutState(true, until - now);
        }

        return LockoutState.Unlocked;
    }

    public LockoutState RegisterFailure(string email)
    {
        var key = Normalize(email);
        var now = clock.GetUtcNow();

        var updated = _byEmail.AddOrUpdate(
            key,
            _ => new Attempts(1, now, null),
            (_, existing) =>
            {
                // A still-active lockout, or failures outside the rolling window, reset the count.
                if (existing.LockedUntil is { } until && now < until)
                {
                    return existing;
                }

                if (now - existing.FirstFailureAt > Window)
                {
                    return new Attempts(1, now, null);
                }

                var count = existing.Count + 1;
                var lockedUntil = count >= MaxAttempts ? now + Lockout : (DateTimeOffset?)null;
                return existing with { Count = count, LockedUntil = lockedUntil };
            });

        return updated.LockedUntil is { } lockUntil && now < lockUntil
            ? new LockoutState(true, lockUntil - now)
            : LockoutState.Unlocked;
    }

    public void Reset(string email) => _byEmail.TryRemove(Normalize(email), out _);

    private static string Normalize(string email) => email.Trim().ToUpperInvariant();
}
