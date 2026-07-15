namespace FcDraft.Infrastructure.Email;

public sealed class BrevoOptions
{
    public const string SectionName = "Brevo";

    public string ApiKey { get; init; } = string.Empty;
    public string SenderEmail { get; init; } = string.Empty;
    public string SenderName { get; init; } = "The Draft Room";
    public string LoginUrl { get; init; } = "http://localhost:5173/login";

    /// <summary>Base URL of the reset-password page; the reset email appends <c>?token=…</c>.</summary>
    public string PasswordResetUrl { get; init; } = "http://localhost:5173/reset-password";
}
