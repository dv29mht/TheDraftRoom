using System.Diagnostics.Metrics;
using FcDraft.Application.Common.Interfaces;

namespace FcDraft.Infrastructure.Observability;

/// <summary>
/// Vendor-neutral §15 product analytics (PR-23) on the same <see cref="DraftRoomMetrics.MeterName"/>
/// meter as the PR-22 operational instruments — observable with
/// <c>dotnet-counters monitor --counters FcDraft.DraftRoom</c> today and exportable through any
/// OpenTelemetry listener later without touching call sites. Tags are restricted to the
/// low-cardinality facts the interface accepts (format/outcome/action); nothing personal, secret,
/// or content-bearing can reach an instrument (§15).
/// </summary>
public sealed class DraftRoomAnalytics : IProductAnalytics
{
    private readonly Counter<long> usersInvited;
    private readonly Counter<long> usersActivated;
    private readonly Counter<long> draftsCreated;
    private readonly Counter<long> draftsStarted;
    private readonly Counter<long> draftsEnded;
    private readonly Histogram<double> timeToFirstPick;
    private readonly Counter<long> picksAccepted;
    private readonly Histogram<double> turnDuration;
    private readonly Counter<long> interventions;
    private readonly Counter<long> hubJoins;
    private readonly Counter<long> emailDeliveries;

    public DraftRoomAnalytics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(DraftRoomMetrics.MeterName);
        usersInvited = meter.CreateCounter<long>(
            "draftroom.users.invited",
            description: "Account invitations issued (creation or re-send).");
        usersActivated = meter.CreateCounter<long>(
            "draftroom.users.activated",
            description: "Accounts that completed the forced first password change.");
        draftsCreated = meter.CreateCounter<long>(
            "draftroom.drafts.created",
            description: "Lobbies created, tagged by format.");
        draftsStarted = meter.CreateCounter<long>(
            "draftroom.drafts.started",
            description: "Drafts started (configuration frozen), tagged by format.");
        draftsEnded = meter.CreateCounter<long>(
            "draftroom.drafts.ended",
            description: "Drafts reaching a terminal state, tagged by format and outcome.");
        timeToFirstPick = meter.CreateHistogram<double>(
            "draftroom.drafts.time_to_first_pick",
            unit: "s",
            description: "Seconds from lobby creation to the first accepted position pick.");
        picksAccepted = meter.CreateCounter<long>(
            "draftroom.picks.accepted",
            description: "Accepted position picks, tagged by format and auto (timer expiry).");
        turnDuration = meter.CreateHistogram<double>(
            "draftroom.picks.turn_duration",
            unit: "s",
            description: "Seconds of the 120 s turn consumed by each accepted position pick.");
        interventions = meter.CreateCounter<long>(
            "draftroom.drafts.interventions",
            description: "Live-draft controls (pause/resume/cancel/recover), tagged by action and actor kind.");
        hubJoins = meter.CreateCounter<long>(
            "draftroom.hub.joins",
            description: "Successful live-hub group joins, tagged initial vs reconnect.");
        emailDeliveries = meter.CreateCounter<long>(
            "draftroom.email.delivery",
            description: "Email delivery attempt outcomes (sent/retry/failed) across both delivery branches.");
    }

    public void UserInvited() => usersInvited.Add(1);

    public void UserActivated() => usersActivated.Add(1);

    public void DraftCreated(string format) =>
        draftsCreated.Add(1, Format(format));

    public void DraftStarted(string format) =>
        draftsStarted.Add(1, Format(format));

    public void DraftEnded(string format, string outcome) =>
        draftsEnded.Add(1, Format(format), new KeyValuePair<string, object?>("outcome", outcome));

    public void FirstPick(string format, double secondsFromCreation) =>
        timeToFirstPick.Record(secondsFromCreation, Format(format));

    public void PickAccepted(string format, bool auto, double? turnSeconds)
    {
        var autoTag = new KeyValuePair<string, object?>("auto", auto);
        picksAccepted.Add(1, Format(format), autoTag);
        if (turnSeconds is { } seconds)
        {
            turnDuration.Record(seconds, Format(format), autoTag);
        }
    }

    public void DraftIntervention(string action, bool byAdmin) =>
        interventions.Add(
            1,
            new KeyValuePair<string, object?>("action", action),
            new KeyValuePair<string, object?>("actor", byAdmin ? "admin" : "host"));

    public void HubJoined(bool reconnect) =>
        hubJoins.Add(1, new KeyValuePair<string, object?>("reconnect", reconnect));

    public void EmailDelivery(string outcome) =>
        emailDeliveries.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    private static KeyValuePair<string, object?> Format(string format) => new("format", format);
}
