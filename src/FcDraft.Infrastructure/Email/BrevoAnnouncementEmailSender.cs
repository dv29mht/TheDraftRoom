using System.Net.Http.Json;
using System.Text.Encodings.Web;
using FcDraft.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace FcDraft.Infrastructure.Email;

/// <summary>
/// Sends one §9.8 announcement email (PR-21) through Brevo's transactional API, mirroring
/// <see cref="BrevoDraftEmailSender"/>. The campaign id travels to Brevo as a tag — the §9.8 campaign
/// metadata — and is stamped on the outbox row, so delivery is traceable end to end. Only the outbox
/// processor (or the in-memory direct queue) calls this; the announcement command never touches Brevo.
/// </summary>
public sealed class BrevoAnnouncementEmailSender(
    HttpClient httpClient,
    IOptions<BrevoOptions> options) : IAnnouncementEmailSender
{
    private readonly BrevoOptions _options = options.Value;

    public async Task SendAsync(
        string email, string displayName, AnnouncementEmailPayload payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.SenderEmail))
        {
            throw new InvalidOperationException(
                "Brevo is not configured. Set Brevo:ApiKey and Brevo:SenderEmail before sending announcements.");
        }

        var link = payload.DraftId.HasValue
            ? $"{AppBaseUrl()}/drafts/{payload.DraftId}"
            : AppBaseUrl();
        var linkText = payload.DraftId.HasValue ? "Open the draft" : "Open The Draft Room";

        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/smtp/email");
        request.Headers.Add("api-key", _options.ApiKey);
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = JsonContent.Create(new
        {
            sender = new { email = _options.SenderEmail, name = _options.SenderName },
            to = new[] { new { email, name = displayName } },
            subject = payload.Subject,
            htmlContent = BuildHtml(displayName, payload.Subject, payload.Body, linkText, link),
            // §9.8 campaign metadata: tie every delivery back to the announcement record.
            tags = new[] { "announcement", payload.CampaignId.ToString("N") },
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Brevo rejected the announcement email ({(int)response.StatusCode}): {responseBody}",
                null,
                response.StatusCode);
        }
    }

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

    private static string BuildHtml(string displayName, string heading, string body, string linkText, string link)
    {
        var encoder = HtmlEncoder.Default;
        var safeName = encoder.Encode(displayName);
        var safeHeading = encoder.Encode(heading);
        // Encode first, then restore the author's line breaks — the only markup a plain announcement gets.
        var safeBody = encoder.Encode(body).Replace("\r\n", "\n").Replace("\n", "<br/>");
        var safeLinkText = encoder.Encode(linkText);
        var safeLink = encoder.Encode(link);
        return $$"""
            <!doctype html>
            <html lang="en">
              <body style="margin:0;background:#f7f7f9;color:#16161c;font-family:Arial,sans-serif">
                <div style="max-width:560px;margin:0 auto;padding:40px 24px">
                  <div style="background:#fff;border:1px solid #e3e3e8;border-radius:16px;padding:32px">
                    <p style="margin:0 0 8px;color:#ff006e;font-weight:700">THE DRAFT ROOM</p>
                    <h1 style="margin:0 0 16px;font-size:28px">{{safeHeading}}</h1>
                    <p>Hi {{safeName}},</p>
                    <p>{{safeBody}}</p>
                    <p><a href="{{safeLink}}" style="display:inline-block;padding:14px 20px;background:#ff006e;color:#16161c;border-radius:10px;font-weight:700;text-decoration:none">{{safeLinkText}}</a></p>
                    <p style="margin-top:24px;color:#666672;font-size:13px">This is an optional announcement from The Draft Room. You can opt out of announcement emails from your profile's email preferences.</p>
                  </div>
                </div>
              </body>
            </html>
            """;
    }
}
