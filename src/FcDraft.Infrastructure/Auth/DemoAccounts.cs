using FcDraft.Domain.Entities;

namespace FcDraft.Infrastructure.Auth;

/// <summary>
/// The deterministic demo player accounts seeded when <c>Database:SeedDemoAccounts</c> is enabled
/// (PR-23). Together with the always-seeded <c>player@draftroom.dev</c> they give the four activated
/// players a minimum 2v2 lobby needs, without depending on live invitation email — the full-stack
/// E2E suites and local demo/device sessions run in environment Testing, where Brevo is
/// deliberately unconfigured. Both storage branches seed the SAME list so evidence and docs agree.
/// Passwords are public in this repository; the flag must stay off in production.
/// </summary>
public static class DemoAccounts
{
    public sealed record DemoAccount(string Email, string DisplayName, string Password);

    public static readonly IReadOnlyList<DemoAccount> Players =
    [
        new("player2@draftroom.dev", "Practice Player Two", "Player2@2026"),
        new("player3@draftroom.dev", "Practice Player Three", "Player3@2026"),
        new("player4@draftroom.dev", "Practice Player Four", "Player4@2026"),
    ];

    public const UserRole Role = UserRole.Player;
}
