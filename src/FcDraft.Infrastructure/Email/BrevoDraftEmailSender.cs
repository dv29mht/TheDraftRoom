using System.Net.Http.Json;
using System.Text.Encodings.Web;
using FcDraft.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace FcDraft.Infrastructure.Email;

/// <summary>
/// Sends the four §9.8 draft-lifecycle templates (PR-20) through Brevo's transactional API, mirroring
/// <see cref="BrevoInvitationEmailSender"/>. Deep links derive from <see cref="BrevoOptions.AppBaseUrl"/>
/// (falling back to the LoginUrl's origin, which production already configures), so no new secret or
/// setting is required for correct links.
/// </summary>
public sealed class BrevoDraftEmailSender(
    HttpClient httpClient,
    IOptions<BrevoOptions> options) : IDraftEmailSender
{
    private readonly BrevoOptions _options = options.Value;

    public Task SendInvitationAsync(string email, string displayName, DraftEmailPayload payload, CancellationToken cancellationToken) =>
        SendAsync(email, displayName, cancellationToken,
            subject: $"You're invited to draft: {payload.DraftName}",
            heading: "You're on the board",
            body: $"You've been invited to the draft “{payload.DraftName}”. Open the lobby and confirm you're in before the host starts.",
            linkText: "Open the lobby",
            link: DraftLink(payload));

    public Task SendReminderAsync(string email, string displayName, DraftEmailPayload payload, CancellationToken cancellationToken) =>
        SendAsync(email, displayName, cancellationToken,
            subject: $"Reminder: {payload.DraftName} needs you",
            heading: "Your draft is waiting",
            body: $"The host sent a nudge — “{payload.DraftName}” can't start without you. Open the lobby and get ready.",
            linkText: "Open the lobby",
            link: DraftLink(payload));

    public Task SendCancelledAsync(string email, string displayName, DraftEmailPayload payload, CancellationToken cancellationToken) =>
        SendAsync(email, displayName, cancellationToken,
            subject: $"Draft cancelled: {payload.DraftName}",
            heading: "This draft was cancelled",
            body: $"“{payload.DraftName}” was cancelled{(string.IsNullOrWhiteSpace(payload.Reason) ? "." : $" — {payload.Reason}")} The full history stays available in the app.",
            linkText: "View the draft",
            link: DraftLink(payload));

    public Task SendCompletedAsync(string email, string displayName, DraftEmailPayload payload, CancellationToken cancellationToken) =>
        SendAsync(email, displayName, cancellationToken,
            subject: $"Results are in: {payload.DraftName}",
            heading: "Every squad is complete",
            body: $"“{payload.DraftName}” has finished. See the final squads, ratings, and the full pick sequence.",
            linkText: "View the results",
            link: $"{DraftLink(payload)}/results");

    private string DraftLink(DraftEmailPayload payload) => $"{AppBaseUrl()}/drafts/{payload.DraftId}";

    private string AppBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.AppBaseUrl))
        {
            return _options.AppBaseUrl.TrimEnd('/');
        }

        // Production already configures LoginUrl for account invitations — reuse its origin.
        return Uri.TryCreate(_options.LoginUrl, UriKind.Absolute, out var login)
            ? login.GetLeftPart(UriPartial.Authority)
            : "http://localhost:5173";
    }

    private async Task SendAsync(
        string email, string displayName, CancellationToken cancellationToken,
        string subject, string heading, string body, string linkText, string link)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.SenderEmail))
        {
            throw new InvalidOperationException(
                "Brevo is not configured. Set Brevo:ApiKey and Brevo:SenderEmail before sending draft emails.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/smtp/email");
        request.Headers.Add("api-key", _options.ApiKey);
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = JsonContent.Create(new
        {
            sender = new { email = _options.SenderEmail, name = _options.SenderName },
            to = new[] { new { email, name = displayName } },
            subject,
            htmlContent = BuildHtml(displayName, heading, body, linkText, link)
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Brevo rejected the draft email ({(int)response.StatusCode}): {responseBody}",
                null,
                response.StatusCode);
        }
    }

    private static string BuildHtml(string displayName, string heading, string body, string linkText, string link)
    {
        var encoder = HtmlEncoder.Default;
        var safeName = encoder.Encode(displayName);
        var safeHeading = encoder.Encode(heading);
        var safeBody = encoder.Encode(body);
        var safeLinkText = encoder.Encode(linkText);
        var safeLink = encoder.Encode(link);
        return $$"""
            <!doctype html>
            <html lang="en">
              <body style="margin:0;background:#f7f7f9;color:#16161c;font-family:Arial,sans-serif">
                <div style="max-width:560px;margin:0 auto;padding:40px 24px">
                  <div style="background:#fff;border:1px solid #e3e3e8;border-radius:16px;padding:32px">
                    <p style="margin:0 0 8px;color:#d4af37;font-weight:700">ROSTR</p>
                    <h1 style="margin:0 0 16px;font-size:28px">{{safeHeading}}</h1>
                    <p>Hi {{safeName}},</p>
                    <p>{{safeBody}}</p>
                    <p><a href="{{safeLink}}" style="display:inline-block;padding:14px 20px;background:#d4af37;color:#16161c;border-radius:10px;font-weight:700;text-decoration:none">{{safeLinkText}}</a></p>
                    <p style="margin-top:24px;color:#666672;font-size:13px">You're receiving this because you take part in drafts on ROSTR.</p>
                  </div>
                </div>
              </body>
            </html>
            """;
    }
}
