namespace FcDraft.API.Middleware;

/// <summary>
/// Stamps the release-hardening headers on every /api response (PR-22, PRD §12.2):
/// <list type="bullet">
/// <item><see cref="ApiContract.HeaderName"/> — the version-handshake value the SPA compares
/// against its compiled-in contract to detect a stale cached shell.</item>
/// <item><c>Cache-Control: no-store</c> — authenticated API responses carry personal and live
/// draft data and must never be cached by the service worker, the browser HTTP cache, or an
/// intermediary. Applied as a default at response start so an endpoint that deliberately sets its
/// own policy (the admin SSE stream's <c>no-cache</c>) keeps it.</item>
/// </list>
/// </summary>
public sealed class ApiResponseHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.OnStarting(static state =>
            {
                var response = ((HttpContext)state).Response;
                response.Headers[ApiContract.HeaderName] = ApiContract.Version.ToString();
                if (string.IsNullOrEmpty(response.Headers.CacheControl))
                {
                    response.Headers.CacheControl = "no-store";
                }
                return Task.CompletedTask;
            }, context);
        }

        return next(context);
    }
}
