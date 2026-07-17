using System.Diagnostics.Metrics;
using FcDraft.Application.Common.Interfaces;

namespace FcDraft.Infrastructure.Observability;

/// <summary>
/// Vendor-neutral operational metrics (PR-22, PRD §12.1) on System.Diagnostics.Metrics. The
/// instruments are observable today with <c>dotnet-counters monitor --counters FcDraft.DraftRoom</c>
/// and become exportable (OpenTelemetry, Prometheus, ...) by adding a listener — no call sites change.
/// ASP.NET Core's built-in http.server.request.duration meter already covers raw HTTP timings;
/// these instruments add the application view (per-MediatR-request durations and failures).
/// </summary>
public sealed class DraftRoomMetrics : IOperationalMetrics
{
    public const string MeterName = "FcDraft.DraftRoom";

    private readonly Histogram<double> requestDuration;
    private readonly Counter<long> requestFailures;
    private readonly Counter<long> unhandledErrors;

    public DraftRoomMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        requestDuration = meter.CreateHistogram<double>(
            "draftroom.request.duration",
            unit: "ms",
            description: "Duration of each handled MediatR request.");
        requestFailures = meter.CreateCounter<long>(
            "draftroom.request.failures",
            description: "MediatR requests that ended in an exception (including validation rejections).");
        unhandledErrors = meter.CreateCounter<long>(
            "draftroom.errors.unhandled",
            description: "Unexpected (5xx-class) errors reported to the error-monitoring seam.");
    }

    public void RecordRequest(string requestName, double elapsedMs, bool succeeded)
    {
        requestDuration.Record(
            elapsedMs,
            new KeyValuePair<string, object?>("request", requestName),
            new KeyValuePair<string, object?>("outcome", succeeded ? "ok" : "error"));
        if (!succeeded)
        {
            requestFailures.Add(1, new KeyValuePair<string, object?>("request", requestName));
        }
    }

    public void RecordUnhandledError(string source) =>
        unhandledErrors.Add(1, new KeyValuePair<string, object?>("source", source));
}
