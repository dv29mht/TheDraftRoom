namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Error-monitoring seam (PR-22, PRD §12.1). Unexpected exceptions are reported here instead of
/// straight to a vendor SDK; the default implementation writes a structured error log and bumps
/// the error metric. Wiring Sentry/Rollbar/etc. later means replacing one DI registration — no
/// call site changes and no vendor lock.
/// </summary>
public interface IErrorReporter
{
    void Report(Exception exception, string? correlationId = null, string? source = null);
}
