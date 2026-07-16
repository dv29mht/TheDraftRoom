using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;

namespace FcDraft.Application.Features.Drafts;

/// <summary>
/// Emits the §9.8/§9.9 participant communications for draft lifecycle moments (PR-20): a persistent
/// per-user notification row plus an outbox-backed email, appended INSIDE the caller's transaction (the
/// command handlers call these methods within their <see cref="ITransactionRunner"/> scope), so a
/// rolled-back mutation never notifies and a mail outage never rolls back a mutation.
///
/// Recorded §9.9 decision: invitations, cancellations, and completion results are ESSENTIAL service
/// messages about the recipient's own participation — they ignore the opt-out. The host-initiated
/// reminder is the OPTIONAL announcement-style nudge — its email honours
/// <see cref="User.OptionalEmailOptOut"/> (the in-app notification still appears).
/// </summary>
public sealed class DraftParticipantNotifier(
    IUserNotificationStore notifications, IEmailQueue emails, IIdentityService identity)
{
    public const string InvitedType = "draft.invited";
    public const string ReminderType = "draft.reminder";
    public const string CancelledType = "draft.cancelled";
    public const string CompletedType = "draft.completed";

    /// <summary>One participant was invited (lobby creation or a later invite).</summary>
    public async Task NotifyInvitedAsync(Draft draft, User invitee, CancellationToken cancellationToken)
    {
        await AddNotificationAsync(
            invitee.Id, InvitedType, draft,
            $"You're invited: {draft.Name}",
            $"You've been invited to the {DraftFormats.ToWire(draft.Format)} draft “{draft.Name}”. Open the lobby and confirm you're in.",
            cancellationToken);
        await emails.EnqueueDraftEmailAsync(
            EmailKind.DraftInvitation, invitee.Email, invitee.DisplayName,
            new DraftEmailPayload(draft.Id, draft.Name), cancellationToken);
        await notifications.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Host-initiated reminder to every other participant. Returns how many were reminded.</summary>
    public async Task<int> NotifyReminderAsync(Draft draft, Guid actorUserId, CancellationToken cancellationToken)
    {
        var reminded = 0;
        foreach (var user in await ParticipantAccountsAsync(draft, cancellationToken))
        {
            if (user.Id == actorUserId)
            {
                continue;
            }

            await AddNotificationAsync(
                user.Id, ReminderType, draft,
                $"Reminder: {draft.Name}",
                $"The draft “{draft.Name}” needs you — open the lobby and get ready.",
                cancellationToken);

            // The optional nudge: the email respects the §9.9 opt-out; the in-app notification always lands.
            if (!user.OptionalEmailOptOut)
            {
                await emails.EnqueueDraftEmailAsync(
                    EmailKind.DraftReminder, user.Email, user.DisplayName,
                    new DraftEmailPayload(draft.Id, draft.Name), cancellationToken);
            }

            reminded++;
        }

        await notifications.SaveChangesAsync(cancellationToken);
        return reminded;
    }

    /// <summary>The draft was cancelled: every participant learns why (essential — ignores the opt-out).</summary>
    public async Task NotifyCancelledAsync(Draft draft, string reason, CancellationToken cancellationToken)
    {
        foreach (var user in await ParticipantAccountsAsync(draft, cancellationToken))
        {
            await AddNotificationAsync(
                user.Id, CancelledType, draft,
                $"Draft cancelled: {draft.Name}",
                $"“{draft.Name}” was cancelled — {reason}",
                cancellationToken);
            await emails.EnqueueDraftEmailAsync(
                EmailKind.DraftCancelled, user.Email, user.DisplayName,
                new DraftEmailPayload(draft.Id, draft.Name, reason), cancellationToken);
        }

        await notifications.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The draft completed: every participant gets the result link (essential — ignores the opt-out).</summary>
    public async Task NotifyCompletedAsync(Draft draft, CancellationToken cancellationToken)
    {
        foreach (var user in await ParticipantAccountsAsync(draft, cancellationToken))
        {
            await AddNotificationAsync(
                user.Id, CompletedType, draft,
                $"Draft complete: {draft.Name}",
                $"“{draft.Name}” has finished — every squad is in. Open the results to see how yours stacks up.",
                cancellationToken);
            await emails.EnqueueDraftEmailAsync(
                EmailKind.DraftCompleted, user.Email, user.DisplayName,
                new DraftEmailPayload(draft.Id, draft.Name), cancellationToken);
        }

        await notifications.SaveChangesAsync(cancellationToken);
    }

    private Task AddNotificationAsync(
        Guid userId, string type, Draft draft, string title, string body, CancellationToken cancellationToken) =>
        notifications.AddAsync(new UserNotification
        {
            UserId = userId,
            Type = type,
            Title = title,
            Body = body,
            DraftId = draft.Id,
        }, cancellationToken);

    private async Task<IReadOnlyList<User>> ParticipantAccountsAsync(Draft draft, CancellationToken cancellationToken)
    {
        var users = new List<User>(draft.Participants.Count);
        foreach (var userId in draft.Participants.Select(participant => participant.UserId).Distinct())
        {
            var user = await identity.FindByIdAsync(userId, cancellationToken);
            if (user is not null)
            {
                users.Add(user);
            }
        }

        return users;
    }
}
