namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Product analytics seam (PR-23, PRD §15). Mirrors <see cref="IOperationalMetrics"/> (PR-22): the
/// default implementation publishes vendor-neutral System.Diagnostics.Metrics instruments on the
/// same <c>FcDraft.DraftRoom</c> meter — swapping the DI registration changes the backend without
/// touching call sites, and no vendor SDK is referenced anywhere.
/// <para>
/// §15 privacy rule: every method deliberately accepts only low-cardinality facts (format, outcome,
/// action, durations). Implementations must never receive or record user ids, emails, names,
/// passwords, tokens, or message content. Analytics are observational — call sites treat the seam
/// as infallible and it must never influence or fail a mutation.
/// </para>
/// </summary>
public interface IProductAnalytics
{
    /// <summary>An account invitation was issued (creation or re-send) — the invite-to-activation numerator.</summary>
    void UserInvited();

    /// <summary>A must-change-password account set its own password — the invite-to-activation denominator.</summary>
    void UserActivated();

    /// <summary>A lobby was created. <paramref name="format"/> is the wire format ("1v1"/"2v2").</summary>
    void DraftCreated(string format);

    /// <summary>A draft started (ReadyCheck → SpinnerRanking) — with <see cref="DraftCreated"/>, the lobby-to-start conversion.</summary>
    void DraftStarted(string format);

    /// <summary>A draft reached a terminal state. <paramref name="outcome"/>: completed | cancelled | abandoned.</summary>
    void DraftEnded(string format, string outcome);

    /// <summary>Seconds from lobby creation to the draft's FIRST accepted position pick (§15 median time to first pick).</summary>
    void FirstPick(string format, double secondsFromCreation);

    /// <summary>
    /// One accepted position pick. <paramref name="auto"/> marks a §6.4 timer expiry;
    /// <paramref name="turnSeconds"/> is how much of the 120 s turn was consumed (null when no clock anchored the turn).
    /// </summary>
    void PickAccepted(string format, bool auto, double? turnSeconds);

    /// <summary>A live-draft control (action: pause | resume | cancel | recover); <paramref name="byAdmin"/> separates admin interventions (§15) from host actions.</summary>
    void DraftIntervention(string action, bool byAdmin);

    /// <summary>A client joined a draft's live group; <paramref name="reconnect"/> marks a successful rejoin after a dropped connection (§15 reconnection success).</summary>
    void HubJoined(bool reconnect);

    /// <summary>One email delivery attempt outcome: sent | retry | failed (§15 delivery rate). Kind/recipient are deliberately not recorded.</summary>
    void EmailDelivery(string outcome);
}

/// <summary>
/// The inert default used when a call site is constructed without analytics (unit tests build
/// handlers directly); DI always supplies the real implementation.
/// </summary>
public sealed class NullProductAnalytics : IProductAnalytics
{
    public static readonly NullProductAnalytics Instance = new();

    public void UserInvited() { }
    public void UserActivated() { }
    public void DraftCreated(string format) { }
    public void DraftStarted(string format) { }
    public void DraftEnded(string format, string outcome) { }
    public void FirstPick(string format, double secondsFromCreation) { }
    public void PickAccepted(string format, bool auto, double? turnSeconds) { }
    public void DraftIntervention(string action, bool byAdmin) { }
    public void HubJoined(bool reconnect) { }
    public void EmailDelivery(string outcome) { }
}
