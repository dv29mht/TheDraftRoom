using System.Text.RegularExpressions;
using FcDraft.Infrastructure.Observability;

namespace FcDraft.API.Middleware;

/// <summary>
/// Assigns every request a correlation id (PR-22): honours a well-formed incoming
/// X-Correlation-Id header (so a client or edge proxy can stitch its own traces through), else
/// generates one. The id is echoed on the response header, pushed into the AsyncLocal accessor
/// (where the MediatR logging behavior reads it), and wrapped around the request as a logging
/// scope so every log line of the request carries it in the structured JSON output.
/// </summary>
public sealed partial class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context, CorrelationIdAccessor accessor)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = WellFormedId().IsMatch(incoming)
            ? incoming
            : Guid.NewGuid().ToString("N");

        accessor.Set(correlationId);
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    // Accept only conservative ids so a hostile header value can never smuggle log-forging or
    // header-splitting characters into scopes and response headers.
    [GeneratedRegex("^[A-Za-z0-9._-]{8,64}$")]
    private static partial Regex WellFormedId();
}
