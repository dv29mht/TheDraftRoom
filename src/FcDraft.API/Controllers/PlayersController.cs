using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Datasets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Controllers;

/// <summary>
/// Read-only player explorer over the active dataset (PR-08). Any signed-in participant may browse.
/// Only eligible men's base/Kick Off footballers rated 75+ are ever returned; excluded and inactive
/// content never appears because the query service filters it at the boundary.
/// </summary>
[ApiController]
[Authorize]
[Route("api/players")]
public sealed class PlayersController(IPlayerQueryService players) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PlayerSearchResult>> Search(
        [FromQuery] string? search,
        [FromQuery] string? position,
        [FromQuery] int? minOverall,
        [FromQuery] int? maxOverall,
        [FromQuery] string? club,
        [FromQuery] string? league,
        [FromQuery] string? nation,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 48,
        CancellationToken cancellationToken = default)
    {
        var query = new PlayerQuery(search, position, minOverall, maxOverall, club, league, nation, sort, page, pageSize);
        return Ok(await players.SearchAsync(query, cancellationToken));
    }

    [HttpGet("filters")]
    public async Task<ActionResult<PlayerFilterOptions>> Filters(CancellationToken cancellationToken) =>
        Ok(await players.GetFilterOptionsAsync(cancellationToken));

    [HttpGet("{externalId:int}")]
    public async Task<ActionResult<PlayerCardDto>> Get(int externalId, CancellationToken cancellationToken)
    {
        var player = await players.GetByExternalIdAsync(externalId, cancellationToken);
        return player is null ? NotFound() : Ok(player);
    }
}
