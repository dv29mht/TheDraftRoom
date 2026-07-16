using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

// The per-user notification shapes (PR-20; extra JSON fields ignored).
public sealed record NotificationRow(
    Guid Id, string Type, string Title, string Body, Guid? DraftId, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt);
public sealed record NotificationsPage(List<NotificationRow> Items, int UnreadCount);
public sealed record EmailPrefs(bool OptionalEmailOptOut);

/// <summary>
/// Drives the PR-20 participant communications over real HTTP: an invite lands a persistent, deep-linking
/// notification with an unread badge; mark-read/mark-all are authorization-scoped (another user's id is a
/// 404); the §9.9 email preference suppresses only the OPTIONAL reminder email (in-app always lands); and
/// a simulated Brevo outage never fails the draft mutation that tried to email.
/// </summary>
public sealed class UserNotificationTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    private const string StrongPassword = "Strong@2026Pass";

    private async Task<(HttpClient Client, Guid UserId)> HostAsync()
    {
        var login = await factory.CreateClient().LoginAsync(SeededAccounts.PlayerEmail, SeededAccounts.PlayerPassword);
        return (factory.CreateClient().WithBearer(login.AccessToken), login.User.Id);
    }

    private async Task<(Guid UserId, HttpClient Client, string Email)> ActivePlayerAsync(string email, string name)
    {
        var admin = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        var adminClient = factory.CreateClient().WithBearer(admin.AccessToken);
        var create = await adminClient.PostAsJsonAsync("/api/users", new { email, displayName = name });
        create.EnsureSuccessStatusCode();
        var userId = (await create.Content.ReadFromJsonAsync<ManagedUser>())!.Id;

        var otp = factory.EmailSender.PasswordFor(email);
        var login = await factory.CreateClient().LoginAsync(email, otp);
        var change = await factory.CreateClient().WithBearer(login.AccessToken)
            .PostAsJsonAsync("/api/auth/change-password", new { currentPassword = otp, newPassword = StrongPassword, confirmPassword = StrongPassword });
        change.EnsureSuccessStatusCode();
        var changed = (await change.Content.ReadFromJsonAsync<LoginResponse>())!;
        return (userId, factory.CreateClient().WithBearer(changed.AccessToken), email);
    }

    private static async Task<LobbyDetail> CreateLobbyAsync(HttpClient host, params Guid[] invites)
    {
        var response = await host.PostAsJsonAsync("/api/drafts", new { name = "Notify Lobby", format = "1v1", inviteUserIds = invites });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LobbyDetail>())!;
    }

    [Fact]
    public async Task An_invite_lands_a_persistent_deep_linking_notification_with_scoped_mark_read()
    {
        var (host, _) = await HostAsync();
        var (guestId, guest, guestEmail) = await ActivePlayerAsync("notify.g1@draftroom.test", "Notify One");

        var lobby = await CreateLobbyAsync(host, guestId);

        // The invitee sees the notification with the draft deep link and an unread badge of 1.
        var inbox = (await guest.GetFromJsonAsync<NotificationsPage>("/api/me/notifications"))!;
        var invite = Assert.Single(inbox.Items, row => row.Type == "draft.invited" && row.DraftId == lobby.Summary.Id);
        Assert.Equal(1, inbox.UnreadCount);
        Assert.Null(invite.ReadAt);

        // The invitation email was captured for the invitee (essential — always sent).
        Assert.Contains(factory.DraftEmailSender.Sent,
            email => email.Template == "invitation" && email.Email == guestEmail && email.Payload.DraftId == lobby.Summary.Id);

        // Someone else cannot read (or discover) the notification: 404, not 403.
        var markByHost = await host.PostAsync($"/api/me/notifications/{invite.Id}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, markByHost.StatusCode);

        // The owner marks it read; the badge clears and the read stamp persists on a fresh list.
        var marked = (await (await guest.PostAsync($"/api/me/notifications/{invite.Id}/read", null))
            .Content.ReadFromJsonAsync<NotificationsPage>())!;
        Assert.Equal(0, marked.UnreadCount);
        var relisted = (await guest.GetFromJsonAsync<NotificationsPage>("/api/me/notifications"))!;
        Assert.NotNull(relisted.Items.Single(row => row.Id == invite.Id).ReadAt);
    }

    [Fact]
    public async Task The_email_opt_out_suppresses_only_the_optional_reminder_email()
    {
        var (host, _) = await HostAsync();
        var (quietId, quiet, quietEmail) = await ActivePlayerAsync("notify.g2@draftroom.test", "Notify Quiet");

        // The §9.9 preference round-trips.
        Assert.False((await quiet.GetFromJsonAsync<EmailPrefs>("/api/me/email-preferences"))!.OptionalEmailOptOut);
        var put = await quiet.PutAsJsonAsync("/api/me/email-preferences", new { optionalEmailOptOut = true });
        Assert.True((await put.Content.ReadFromJsonAsync<EmailPrefs>())!.OptionalEmailOptOut);

        var lobby = await CreateLobbyAsync(host, quietId);
        var remind = await host.PostAsync($"/api/drafts/{lobby.Summary.Id}/remind", null);
        remind.EnsureSuccessStatusCode();

        // In-app reminder always lands; the OPTIONAL reminder email respected the opt-out.
        var inbox = (await quiet.GetFromJsonAsync<NotificationsPage>("/api/me/notifications"))!;
        Assert.Single(inbox.Items, row => row.Type == "draft.reminder" && row.DraftId == lobby.Summary.Id);
        Assert.DoesNotContain(factory.DraftEmailSender.Sent,
            email => email.Template == "reminder" && email.Email == quietEmail);
        // The essential invitation email still went out despite the opt-out.
        Assert.Contains(factory.DraftEmailSender.Sent,
            email => email.Template == "invitation" && email.Email == quietEmail);
    }

    [Fact]
    public async Task A_brevo_outage_never_fails_the_draft_mutation_that_tried_to_email()
    {
        var (host, _) = await HostAsync();
        var (guestId, guest, _) = await ActivePlayerAsync("notify.g3@draftroom.test", "Notify Down");

        var lobby = await CreateLobbyAsync(host, guestId);

        // Every send fails from here on — the cancellation must still commit.
        factory.DraftEmailSender.FailuresRemaining = 10;
        try
        {
            var cancel = await host.PostAsJsonAsync(
                $"/api/drafts/{lobby.Summary.Id}/cancel",
                new { reason = "Mail is down", expectedVersion = lobby.Summary.Version });
            cancel.EnsureSuccessStatusCode();
            var detail = (await cancel.Content.ReadFromJsonAsync<LobbyDetail>())!;
            Assert.Equal("Cancelled", detail.Summary.Status);
        }
        finally
        {
            factory.DraftEmailSender.FailuresRemaining = 0;
        }

        // The in-app notification committed with the mutation even though every email failed.
        var inbox = (await guest.GetFromJsonAsync<NotificationsPage>("/api/me/notifications"))!;
        Assert.Single(inbox.Items, row => row.Type == "draft.cancelled" && row.DraftId == lobby.Summary.Id);
    }

}
