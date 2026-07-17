using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Controllers;

/// <summary>
/// Anonymous service metadata (PR-22). The SPA polls <c>GET /api/meta/version</c> (never cached —
/// /api is excluded from the service worker and stamped no-store) to run the version handshake
/// even before sign-in; <c>revision</c> surfaces the Cloud Run revision serving the request so a
/// deploy is verifiable from the outside.
/// </summary>
[ApiController]
[Route("api/meta")]
public sealed class MetaController : ControllerBase
{
    [HttpGet("version")]
    [AllowAnonymous]
    public IActionResult Version() => Ok(new
    {
        service = "fc-draft-api",
        contract = ApiContract.Version,
        // Cloud Run injects K_REVISION into every instance; local hosts report "local".
        revision = Environment.GetEnvironmentVariable("K_REVISION") ?? "local"
    });
}
