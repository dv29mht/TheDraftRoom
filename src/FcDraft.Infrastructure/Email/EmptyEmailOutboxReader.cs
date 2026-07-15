using FcDraft.Application.Common.Interfaces;

namespace FcDraft.Infrastructure.Email;

/// <summary>
/// Outbox reader for the in-memory foundation, where email is delivered inline and there is no
/// durable outbox to inspect. Always reports an empty list so the admin observability endpoint works
/// uniformly in both configurations.
/// </summary>
public sealed class EmptyEmailOutboxReader : IEmailOutboxReader
{
    public Task<IReadOnlyList<EmailOutboxStatusView>> GetRecentAsync(int count, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmailOutboxStatusView>>([]);
}
