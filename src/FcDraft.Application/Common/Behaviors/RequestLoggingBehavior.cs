using System.Diagnostics;
using FcDraft.Application.Common.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FcDraft.Application.Common.Behaviors;

/// <summary>
/// Outermost pipeline behavior (PR-22): every MediatR request emits one structured log line —
/// request name, duration, outcome, correlation id — and one metrics sample. Failures log the
/// exception TYPE only; <c>GlobalExceptionMiddleware</c> owns full exception logging so nothing
/// is double-reported.
/// </summary>
public sealed class RequestLoggingBehavior<TRequest, TResponse>(
    ILogger<RequestLoggingBehavior<TRequest, TResponse>> logger,
    ICorrelationIdAccessor correlation,
    IOperationalMetrics metrics)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var start = Stopwatch.GetTimestamp();
        try
        {
            var response = await next();
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            metrics.RecordRequest(requestName, elapsedMs, succeeded: true);
            logger.LogInformation(
                "Handled {RequestName} in {ElapsedMs}ms (correlation {CorrelationId})",
                requestName,
                Math.Round(elapsedMs, 1),
                correlation.CorrelationId);
            return response;
        }
        catch (Exception exception)
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            metrics.RecordRequest(requestName, elapsedMs, succeeded: false);
            logger.LogWarning(
                "Failed {RequestName} after {ElapsedMs}ms with {ExceptionType} (correlation {CorrelationId})",
                requestName,
                Math.Round(elapsedMs, 1),
                exception.GetType().Name,
                correlation.CorrelationId);
            throw;
        }
    }
}
