using System.Text.Json;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Datasets;
using FcDraft.Infrastructure.Datasets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Controllers;

/// <summary>
/// Admin dataset management (PR-07): import a version (as a draft) from the bundled snapshot or an
/// uploaded FC 26 JSON payload, inspect its validation issues, activate a clean version, and list
/// retained versions. Thin controller; validation and persistence live in <see cref="IDatasetAdminService"/>.
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/datasets")]
public sealed class AdminDatasetsController(IDatasetAdminService datasets) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DatasetVersionSummary>>> List(CancellationToken cancellationToken) =>
        Ok(await datasets.ListVersionsAsync(cancellationToken));

    [HttpGet("{versionId:guid}")]
    public async Task<ActionResult<DatasetVersionDetail>> Get(Guid versionId, CancellationToken cancellationToken)
    {
        var detail = await datasets.GetVersionAsync(versionId, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("import-bundled")]
    public async Task<ActionResult<DatasetImportReport>> ImportBundled(CancellationToken cancellationToken) =>
        Ok(await datasets.ImportBundledAsync(cancellationToken));

    [HttpPost("upload")]
    public async Task<ActionResult<DatasetImportReport>> Upload(
        [FromBody] JsonElement body,
        CancellationToken cancellationToken)
    {
        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("players", out var players)
            || players.ValueKind != JsonValueKind.Array)
        {
            return ValidationProblem("The dataset must be an object with a 'players' array.");
        }

        var parsed = DatasetJsonParser.Parse(body);
        var request = new DatasetImportRequest(parsed.Label, parsed.Source, parsed.Rows);
        return Ok(await datasets.ImportAsync(request, cancellationToken));
    }

    [HttpPost("{versionId:guid}/activate")]
    public async Task<ActionResult<DatasetVersionSummary>> Activate(Guid versionId, CancellationToken cancellationToken)
    {
        if (await datasets.GetVersionAsync(versionId, cancellationToken) is null)
        {
            return NotFound();
        }

        return Ok(await datasets.ActivateAsync(versionId, cancellationToken));
    }
}
