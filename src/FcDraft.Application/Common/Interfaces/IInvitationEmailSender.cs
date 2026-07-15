namespace FcDraft.Application.Common.Interfaces;

public interface IInvitationEmailSender
{
    Task SendAsync(
        string email,
        string displayName,
        string temporaryPassword,
        CancellationToken cancellationToken);
}
