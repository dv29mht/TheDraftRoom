using FcDraft.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FcDraft.Infrastructure.Observability;

/// <summary>
/// Default error-monitoring hook (PR-22): writes one structured error log entry (which Cloud Run
/// surfaces in Error Reporting via the JSON console logs) and bumps the unhandled-error metric.
/// This is the vendor seam — pointing the platform at Sentry or similar later means registering a
/// different <see cref="IErrorReporter"/>; nothing else changes.
/// </summary>
public sealed class LoggingErrorReporter(
    ILogger<LoggingErrorReporter> logger,
    IOperationalMetrics metrics) : IErrorReporter
{
    public void Report(Exception exception, string? correlationId = null, string? source = null)
    {
        metrics.RecordUnhandledError(source ?? "unspecified");
        logger.LogError(
            exception,
            "Unhandled error from {Source} (correlation {CorrelationId})",
            source ?? "unspecified",
            correlationId);
    }
}
