namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Exposes the correlation id of the request currently flowing through the pipeline (PR-22).
/// The API's correlation middleware assigns one per HTTP request (honouring an incoming
/// X-Correlation-Id header); handlers and behaviors read it here so a single id links the
/// request log, the MediatR handler logs, and the response header.
/// </summary>
public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; }
}
