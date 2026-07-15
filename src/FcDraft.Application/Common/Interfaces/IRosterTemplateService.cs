using FcDraft.Application.Features.Rosters;

namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Manages versioned roster templates (PR-09). The active template's ordered slots are what a draft
/// snapshots at start; editing templates never mutates an in-progress draft. Backed by the database;
/// the in-memory foundation exposes the default 4-3-3 template read-only.
/// </summary>
public interface IRosterTemplateService
{
    Task<IReadOnlyList<RosterTemplateSummary>> ListAsync(CancellationToken cancellationToken);
    Task<RosterTemplateDetail?> GetAsync(Guid templateId, CancellationToken cancellationToken);
    Task<RosterTemplateDetail?> GetActiveAsync(CancellationToken cancellationToken);
    Task<RosterTemplateSummary> CreateAsync(CreateRosterTemplateRequest request, CancellationToken cancellationToken);
    Task<RosterTemplateSummary> ActivateAsync(Guid templateId, CancellationToken cancellationToken);
}

/// <summary>
/// Reads clubs from the active dataset and curates which are eligible five-star Kick Off clubs (the
/// source feed omits club star ratings, so eligibility is admin-managed). Only eligible clubs from the
/// pinned/active dataset are returned to draft flows.
/// </summary>
public interface IClubDirectoryService
{
    Task<IReadOnlyList<ClubDto>> ListAsync(string? search, CancellationToken cancellationToken);
    Task<IReadOnlyList<ClubDto>> ListEligibleAsync(CancellationToken cancellationToken);
    Task<ClubDto> SetFiveStarEligibilityAsync(Guid clubId, bool eligible, CancellationToken cancellationToken);
}
