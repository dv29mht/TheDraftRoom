namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Operational metrics seam (PR-22, PRD §12.1). The default implementation publishes
/// System.Diagnostics.Metrics instruments (vendor-neutral; readable by dotnet-counters or any
/// OpenTelemetry exporter added later) — swapping the DI registration changes the backend
/// without touching call sites.
/// </summary>
public interface IOperationalMetrics
{
    /// <summary>Records one handled MediatR request: name, wall-clock duration, and outcome.</summary>
    void RecordRequest(string requestName, double elapsedMs, bool succeeded);

    /// <summary>Counts an unexpected (5xx-class) error surfaced to the error reporter.</summary>
    void RecordUnhandledError(string source);
}
