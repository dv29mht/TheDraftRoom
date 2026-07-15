using System.Net.Http.Json;
using System.Text.Encodings.Web;
using FcDraft.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace FcDraft.Infrastructure.Email;

/// <summary>
/// Sends the transactional password-reset email through Brevo. Mirrors
/// <see cref="BrevoInvitationEmailSender"/>. The emailed link carries the single-use reset token as
/// a query parameter; the plaintext token is never persisted server-side.
/// </summary>
public sealed class BrevoPasswordResetEmailSender(
    HttpClient httpClient,
    IOptions<BrevoOptions> options) : IPasswordResetEmailSender
{
    private readonly BrevoOptions _options = options.Value;

    public async Task SendAsync(
        string email,
        string displayName,
        string resetToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.SenderEmail))
        {
            throw new InvalidOperationException(
                "Brevo is not configured. Set Brevo:ApiKey and Brevo:SenderEmail before sending password resets.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/smtp/email");
        request.Headers.Add("api-key", _options.ApiKey);
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = JsonContent.Create(new
        {
            sender = new { email = _options.SenderEmail, name = _options.SenderName },
            to = new[] { new { email, name = displayName } },
            subject = "Reset your Draft Room password",
            htmlContent = BuildHtml(displayName, resetToken)
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Brevo rejected the password reset ({(int)response.StatusCode}): {responseBody}",
                null,
                response.StatusCode);
        }
    }

    private string BuildHtml(string displayName, string resetToken)
    {
        var encoder = HtmlEncoder.Default;
        var safeName = encoder.Encode(displayName);
        var separator = _options.PasswordResetUrl.Contains('?') ? "&" : "?";
        var resetLink = encoder.Encode($"{_options.PasswordResetUrl}{separator}token={Uri.EscapeDataString(resetToken)}");
        return $$"""
            <!doctype html>
            <html lang="en">
              <body style="margin:0;background:#f7f7f9;color:#16161c;font-family:Arial,sans-serif">
                <div style="max-width:560px;margin:0 auto;padding:40px 24px">
                  <div style="background:#fff;border:1px solid #e3e3e8;border-radius:16px;padding:32px">
                    <p style="margin:0 0 8px;color:#ff006e;font-weight:700">THE DRAFT ROOM</p>
                    <h1 style="margin:0 0 16px;font-size:28px">Reset your password</h1>
                    <p>Hi {{safeName}}, we received a request to reset your password.</p>
                    <p>This link is valid for one hour and can be used once.</p>
                    <p><a href="{{resetLink}}" style="display:inline-block;padding:14px 20px;background:#ff006e;color:#16161c;border-radius:10px;font-weight:700;text-decoration:none">Choose a new password</a></p>
                    <p style="margin-top:24px;color:#666672;font-size:13px">If you didn't request this, you can ignore this email and your password will stay the same.</p>
                  </div>
                </div>
              </body>
            </html>
            """;
    }
}
