namespace FcDraft.Application.Features.Auth;

public sealed record AuthUserDto(Guid Id, string DisplayName, string Email, string Role);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    bool MustChangePassword,
    AuthUserDto User);
