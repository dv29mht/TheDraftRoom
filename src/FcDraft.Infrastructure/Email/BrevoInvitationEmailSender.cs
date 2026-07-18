using System.Net.Http.Json;
using System.Text.Encodings.Web;
using FcDraft.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace FcDraft.Infrastructure.Email;

public sealed class BrevoInvitationEmailSender(
    HttpClient httpClient,
    IOptions<BrevoOptions> options) : IInvitationEmailSender
{
    private readonly BrevoOptions _options = options.Value;

    public async Task SendAsync(
        string email,
        string displayName,
        string temporaryPassword,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.SenderEmail))
        {
            throw new InvalidOperationException(
                "Brevo is not configured. Set Brevo:ApiKey and Brevo:SenderEmail before sending invitations.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/smtp/email");
        request.Headers.Add("api-key", _options.ApiKey);
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = JsonContent.Create(new
        {
            sender = new { email = _options.SenderEmail, name = _options.SenderName },
            to = new[] { new { email, name = displayName } },
            subject = "You're invited to ROSTR",
            htmlContent = BuildHtml(displayName, temporaryPassword)
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Brevo rejected the invitation ({(int)response.StatusCode}): {responseBody}",
                null,
                response.StatusCode);
        }
    }

    private string BuildHtml(string displayName, string temporaryPassword)
    {
        var encoder = HtmlEncoder.Default;
        var safeName = encoder.Encode(displayName);
        var safePassword = encoder.Encode(temporaryPassword);
        var safeLoginUrl = encoder.Encode(_options.LoginUrl);
        return $$"""
            <!doctype html>
            <html lang="en">
              <body style="margin:0;background:#f7f7f9;color:#16161c;font-family:Arial,sans-serif">
                <div style="max-width:560px;margin:0 auto;padding:40px 24px">
                  <div style="background:#fff;border:1px solid #e3e3e8;border-radius:16px;padding:32px">
                    <p style="margin:0 0 8px;color:#d4af37;font-weight:700">ROSTR</p>
                    <h1 style="margin:0 0 16px;font-size:28px">You're invited</h1>
                    <p>Hi {{safeName}}, an administrator created an account for you.</p>
                    <p>Sign in with this one-time password. You'll be asked to replace it immediately.</p>
                    <p style="padding:14px 16px;background:#f0f0f4;border-radius:10px;font-size:18px;font-weight:700;letter-spacing:1px">{{safePassword}}</p>
                    <p><a href="{{safeLoginUrl}}" style="display:inline-block;padding:14px 20px;background:#d4af37;color:#16161c;border-radius:10px;font-weight:700;text-decoration:none">Open ROSTR</a></p>
                    <p style="margin-top:24px;color:#666672;font-size:13px">If you weren't expecting this invitation, you can ignore this email.</p>
                  </div>
                </div>
              </body>
            </html>
            """;
    }
}
