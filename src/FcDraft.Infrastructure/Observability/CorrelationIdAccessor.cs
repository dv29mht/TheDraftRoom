using FcDraft.Application.Common.Interfaces;

namespace FcDraft.Infrastructure.Observability;

/// <summary>
/// AsyncLocal-backed correlation id store (PR-22). The API's correlation middleware calls
/// <see cref="Set"/> at the start of every HTTP request; the value flows with the async execution
/// context into MediatR behaviors, handlers, and background continuations of that request without
/// any HttpContext dependency below the API layer.
/// </summary>
public sealed class CorrelationIdAccessor : ICorrelationIdAccessor
{
    // Instance (not static) so each host's singleton is isolated — parallel test hosts in one
    // process never observe each other's ids.
    private readonly AsyncLocal<string?> current = new();

    public string? CorrelationId => current.Value;

    public void Set(string? correlationId) => current.Value = correlationId;
}
