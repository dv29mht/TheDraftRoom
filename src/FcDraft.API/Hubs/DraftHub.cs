using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FcDraft.API.Hubs;

/// <summary>
/// The authenticated live-draft hub (PR-17, PRD §17.7). Clients join one group per draft id and then
/// receive a <c>DraftUpdated</c> message — the version-stamped envelope published by
/// <see cref="IDraftNotifier"/> — for every accepted mutation, timer auto-pick, and host control. Joining
/// is authorized exactly like <c>GET /api/drafts/{id}</c> (participant, host, or admin; a 404-equivalent
/// rejection for anyone else, so a lobby's existence is not leaked), and the JWT arrives over the
/// websocket via the <c>access_token</c> query string (see the <c>OnMessageReceived</c> hook in Program).
/// The hub is transport only: every rule stays in the MediatR handlers, and REST remains the
/// authoritative snapshot a reconnecting client reconciles against.
/// </summary>
[Authorize]
public sealed class DraftHub(ISender sender) : Hub
{
    public static string GroupName(Guid draftId) => $"draft:{draftId}";

    /// <summary>
    /// Joins the caller to the draft's group and returns the authoritative snapshot, so a (re)connecting
    /// client reconciles its state and version in the same round-trip without a separate fetch.
    /// </summary>
    public async Task<DraftDetail> JoinDraft(Guid draftId)
    {
        // Parity with ForcedPasswordChangeMiddleware, which only guards /api paths: a token that still
        // must change its password may not use the live channel either.
        if (string.Equals(
                Context.User?.FindFirst(DraftClaimTypes.PasswordChangeRequired)?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new HubException("Set a new password before using the rest of the app.");
        }

        var detail = await sender.Send(new GetDraftQuery(draftId), Context.ConnectionAborted);
        if (detail is null || !CallerMaySee(detail))
        {
            // The 404-equivalent: outsiders learn nothing about whether the draft exists.
            throw new HubException("This draft does not exist, or you are not one of its participants.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(draftId), Context.ConnectionAborted);
        return detail;
    }

    public Task LeaveDraft(Guid draftId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(draftId), Context.ConnectionAborted);

    private bool CallerMaySee(DraftDetail detail) =>
        CallerIsAdmin
        || detail.Summary.HostUserId == CallerId
        || detail.Participants.Any(participant => participant.UserId == CallerId);

    private bool CallerIsAdmin => Context.User?.IsInRole("admin") == true;

    private Guid CallerId =>
        Guid.TryParse(
            Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out var id)
            ? id
            : throw new HubException("The authenticated token has no subject claim.");
}
