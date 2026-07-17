using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

// PR-21 wire shapes (extra JSON fields ignored).
public sealed record AnnouncementPreviewBody(
    string Subject, string Body, string Audience, Guid? DraftId, string? DraftName,
    string AudienceLabel, int RecipientCount, int EmailRecipientCount, int OptedOutCount);
public sealed record AnnouncementPreviewEnvelope(
    AnnouncementPreviewBody Preview, string SenderName, string? SenderEmail, bool EmailConfigured);
public sealed record AnnouncementRow(
    Guid Id, string Subject, string Body, string Audience, Guid? DraftId, string AudienceLabel,
    int RecipientCount, int EmailCount, int OptedOutCount,
    Guid RequestedByUserId, string RequestedByEmail, DateTimeOffset RequestedAt,
    int EmailsPending, int EmailsSent, int EmailsFailed);
public sealed record DraftAuditRow(
    Guid DraftId, string DraftName, string DraftCode, int Sequence, string Type,
    string? FromStatus, string? ToStatus, int Version, Guid? ActorUserId, string? ActorName,
    string? Reason, DateTimeOffset CreatedAt);
public sealed record SecurityAuditRow(
    Guid Id, string Action, Guid? UserId, string? Email, string? Detail, string? IpAddress, DateTimeOffset CreatedAt);
public sealed record OutboxRow(
    Guid Id, string Kind, string ToEmail, string Status, int AttemptCount, string? LastError,
    DateTimeOffset CreatedAt, DateTimeOffset? SentAt, DateTimeOffset NextAttemptAt, Guid? CampaignId);

/// <summary>
/// Drives the PR-21 admin surfaces over real HTTP against the in-memory host: the §9.8 announcement
/// preview → confirm → send flow (opt-out respected, audience-count 409, Brevo-outage tolerance,
/// campaign delivery visibility), the §9.10 audit views (filters, attribution), and the audit trail's
/// immutability guarantees (read-only routes; no update/delete verb exists anywhere).
/// </summary>
public sealed class AdminCommunicationsAndAuditTests(DraftRoomApiFactory factory)
    : IClassFixture<DraftRoomApiFactory>
{
    private const string StrongPassword = "Strong@2026Pass";

    private async Task<(HttpClient Client, Guid UserId, string Email)> AdminAsync()
    {
        var login = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        return (factory.CreateClient().WithBearer(login.AccessToken), login.User.Id, login.User.Email);
    }

    private async Task<(Guid UserId, HttpClient Client, string Email)> ActivePlayerAsync(string email, string name)
    {
        var (admin, _, _) = await AdminAsync();
        var create = await admin.PostAsJsonAsync("/api/users", new { email, displayName = name });
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

    [Fact]
    public async Task A_previewed_confirmed_announcement_notifies_everyone_and_respects_the_email_opt_out()
    {
        var (admin, adminId, adminEmail) = await AdminAsync();
        var (_, reader, readerEmail) = await ActivePlayerAsync("comm.reader@draftroom.test", "Comm Reader");
        var (_, quiet, quietEmail) = await ActivePlayerAsync("comm.quiet@draftroom.test", "Comm Quiet");

        // The quiet player opts out of OPTIONAL emails (PR-20 preference) — announcements are optional.
        var optOut = await quiet.PutAsJsonAsync("/api/me/email-preferences", new { optionalEmailOptOut = true });
        optOut.EnsureSuccessStatusCode();

        // Preview: subject, sender, audience count, and the opt-out split — reviewed before any send.
        var previewResponse = await admin.PostAsJsonAsync("/api/admin/announcements/preview",
            new { subject = "Season 2 opens", body = "New dataset live.", audience = "all", draftId = (Guid?)null });
        previewResponse.EnsureSuccessStatusCode();
        var preview = (await previewResponse.Content.ReadFromJsonAsync<AnnouncementPreviewEnvelope>())!;
        Assert.Equal("All active players", preview.Preview.AudienceLabel);
        Assert.Equal(1, preview.Preview.OptedOutCount);
        Assert.Equal(preview.Preview.RecipientCount - 1, preview.Preview.EmailRecipientCount);

        // The confirmed send — against exactly the previewed count.
        var sendResponse = await admin.PostAsJsonAsync("/api/admin/announcements",
            new { subject = "Season 2 opens", body = "New dataset live.", audience = "all", draftId = (Guid?)null, confirmedRecipientCount = preview.Preview.RecipientCount });
        sendResponse.EnsureSuccessStatusCode();
        var sent = (await sendResponse.Content.ReadFromJsonAsync<AnnouncementRow>())!;
        Assert.Equal(preview.Preview.RecipientCount, sent.RecipientCount);
        Assert.Equal(adminId, sent.RequestedByUserId);
        Assert.Equal(adminEmail, sent.RequestedByEmail, ignoreCase: true);
        // In-memory delivery is inline: the response already shows every email sent, none pending.
        Assert.Equal(sent.EmailCount, sent.EmailsSent);
        Assert.Equal(0, sent.EmailsPending);

        // The in-app notification lands for BOTH players — including the opted-out one.
        foreach (var client in new[] { reader, quiet })
        {
            var inbox = (await client.GetFromJsonAsync<NotificationsPage>("/api/me/notifications"))!;
            Assert.Contains(inbox.Items, row => row.Type == "announcement" && row.Title == "Season 2 opens");
        }

        // The email respects the opt-out and carries the campaign id (§9.8 metadata). Scope to THIS
        // campaign — the factory (and its capture) is shared by every test in the class.
        var emails = factory.AnnouncementEmailSender.Sent
            .Where(email => email.Payload.CampaignId == sent.Id)
            .ToArray();
        Assert.Equal(sent.EmailCount, emails.Length);
        Assert.Contains(emails, email => email.Email.Equals(readerEmail, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(emails, email => email.Email.Equals(quietEmail, StringComparison.OrdinalIgnoreCase));

        // Delivery visibility (§9.8): the campaign lists with live tallies, and the outbox view shows
        // the per-recipient rows stamped with the campaign id.
        var campaigns = (await admin.GetFromJsonAsync<List<AnnouncementRow>>("/api/admin/announcements"))!;
        var campaign = Assert.Single(campaigns, row => row.Id == sent.Id);
        Assert.Equal(sent.EmailCount, campaign.EmailsSent);
        var outbox = (await admin.GetFromJsonAsync<List<OutboxRow>>("/api/admin/email-outbox"))!;
        Assert.Contains(outbox, row => row.CampaignId == sent.Id && row.Status == "Sent");

        // §9.10: the bulk email request is an audited admin action attributed to the actor.
        var audit = (await admin.GetFromJsonAsync<List<SecurityAuditRow>>(
            "/api/admin/audit/security-events?action=AnnouncementSent"))!;
        var entry = audit.First();
        Assert.Equal(adminId, entry.UserId);
        Assert.Contains("Season 2 opens", entry.Detail);
    }

    [Fact]
    public async Task A_send_confirmed_against_a_stale_audience_count_is_a_409_and_sends_nothing()
    {
        var (admin, _, _) = await AdminAsync();

        var before = factory.AnnouncementEmailSender.Sent.Count;
        var response = await admin.PostAsJsonAsync("/api/admin/announcements",
            new { subject = "Stale", body = "Never lands.", audience = "all", draftId = (Guid?)null, confirmedRecipientCount = 987 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, factory.AnnouncementEmailSender.Sent.Count);
        var campaigns = (await admin.GetFromJsonAsync<List<AnnouncementRow>>("/api/admin/announcements"))!;
        Assert.DoesNotContain(campaigns, row => row.Subject == "Stale");
    }

    [Fact]
    public async Task A_simulated_brevo_outage_never_fails_a_confirmed_announcement()
    {
        var (admin, _, _) = await AdminAsync();

        var previewResponse = await admin.PostAsJsonAsync("/api/admin/announcements/preview",
            new { subject = "Outage-proof", body = "Still lands in-app.", audience = "all", draftId = (Guid?)null });
        var preview = (await previewResponse.Content.ReadFromJsonAsync<AnnouncementPreviewEnvelope>())!;

        factory.AnnouncementEmailSender.FailuresRemaining = 1;
        var sendResponse = await admin.PostAsJsonAsync("/api/admin/announcements",
            new { subject = "Outage-proof", body = "Still lands in-app.", audience = "all", draftId = (Guid?)null, confirmedRecipientCount = preview.Preview.RecipientCount });
        sendResponse.EnsureSuccessStatusCode();
        var sent = (await sendResponse.Content.ReadFromJsonAsync<AnnouncementRow>())!;

        // The failed delivery is VISIBLE, not fatal: one failed tally, the rest sent.
        Assert.Equal(1, sent.EmailsFailed);
        var outbox = (await admin.GetFromJsonAsync<List<OutboxRow>>("/api/admin/email-outbox"))!;
        Assert.Contains(outbox, row => row.CampaignId == sent.Id && row.Status == "Failed" && row.LastError != null);
    }

    [Fact]
    public async Task Announcement_validation_rejects_missing_fields_and_a_draft_audience_without_a_draft()
    {
        var (admin, _, _) = await AdminAsync();

        var noSubject = await admin.PostAsJsonAsync("/api/admin/announcements/preview",
            new { subject = "", body = "b", audience = "all", draftId = (Guid?)null });
        Assert.Equal(HttpStatusCode.BadRequest, noSubject.StatusCode);

        var noDraft = await admin.PostAsJsonAsync("/api/admin/announcements/preview",
            new { subject = "s", body = "b", audience = "draft", draftId = (Guid?)null });
        Assert.Equal(HttpStatusCode.BadRequest, noDraft.StatusCode);

        var unknownDraft = await admin.PostAsJsonAsync("/api/admin/announcements/preview",
            new { subject = "s", body = "b", audience = "draft", draftId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, unknownDraft.StatusCode);
    }

    [Fact]
    public async Task The_admin_communications_and_audit_surfaces_are_admin_only()
    {
        var (_, player, _) = await ActivePlayerAsync("comm.player@draftroom.test", "Comm Player");

        foreach (var path in new[]
                 {
                     "/api/admin/announcements",
                     "/api/admin/audit/draft-events",
                     "/api/admin/audit/security-events",
                     "/api/admin/email-outbox",
                 })
        {
            var read = await player.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        }

        var send = await player.PostAsJsonAsync("/api/admin/announcements",
            new { subject = "s", body = "b", audience = "all", draftId = (Guid?)null, confirmedRecipientCount = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, send.StatusCode);
    }

    [Fact]
    public async Task Audit_records_cannot_be_edited_or_deleted_through_any_normal_route()
    {
        var (admin, _, _) = await AdminAsync();

        // The audit and campaign surfaces expose no mutation verbs at all. An unmatched verb never
        // reaches a handler — the host answers 405 (method known, not allowed) or 404 (the request
        // fell through to the not-found handling). Either way: no update/delete path exists.
        static void AssertNoRoute(HttpResponseMessage response) =>
            Assert.True(
                response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound,
                $"Expected no mutation route, but got {(int)response.StatusCode}.");

        foreach (var path in new[] { "/api/admin/audit/draft-events", "/api/admin/audit/security-events" })
        {
            AssertNoRoute(await admin.DeleteAsync(path));
            AssertNoRoute(await admin.PutAsJsonAsync(path, new { }));
            AssertNoRoute(await admin.PostAsJsonAsync(path, new { }));
        }

        AssertNoRoute(await admin.DeleteAsync("/api/admin/announcements"));
        AssertNoRoute(await admin.PutAsJsonAsync("/api/admin/announcements", new { }));
        AssertNoRoute(await admin.DeleteAsync("/api/admin/email-outbox"));
    }

    [Fact]
    public async Task Admin_user_actions_land_in_the_audit_trail_with_actor_attribution()
    {
        var (admin, adminId, _) = await AdminAsync();
        var email = "audit.subject@draftroom.test";
        var create = await admin.PostAsJsonAsync("/api/users", new { email, displayName = "Audit Subject" });
        create.EnsureSuccessStatusCode();
        var userId = (await create.Content.ReadFromJsonAsync<ManagedUser>())!.Id;

        var deactivate = await admin.PostAsync($"/api/users/{userId}/deactivate", null);
        deactivate.EnsureSuccessStatusCode();

        var created = (await admin.GetFromJsonAsync<List<SecurityAuditRow>>(
            "/api/admin/audit/security-events?action=UserCreated"))!;
        Assert.Contains(created, row => row.UserId == adminId && row.Detail!.Contains(email));

        var deactivated = (await admin.GetFromJsonAsync<List<SecurityAuditRow>>(
            "/api/admin/audit/security-events?action=AccountDeactivated"))!;
        Assert.Contains(deactivated, row => row.UserId == adminId && row.Detail!.Contains(email));

        // The email filter narrows to the acting admin; a future from-date excludes everything.
        var byEmail = (await admin.GetFromJsonAsync<List<SecurityAuditRow>>(
            $"/api/admin/audit/security-events?action=UserCreated&email={SeededAccounts.AdminEmail}"))!;
        Assert.NotEmpty(byEmail);
        var future = (await admin.GetFromJsonAsync<List<SecurityAuditRow>>(
            $"/api/admin/audit/security-events?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}"))!;
        Assert.Empty(future);
    }
}
