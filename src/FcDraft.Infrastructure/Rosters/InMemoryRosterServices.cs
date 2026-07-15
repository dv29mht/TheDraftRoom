using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Rosters;

namespace FcDraft.Infrastructure.Rosters;

/// <summary>
/// Roster templates for the in-memory foundation: the locked default 4-3-3 is exposed read-only.
/// Creating or reactivating templates requires the database.
/// </summary>
public sealed class InMemoryRosterTemplateService : IRosterTemplateService
{
    private static readonly Guid DefaultId = new("00000000-0000-0000-0000-0000000000f1");

    private const string RequiresDatabase = "Roster template management requires the PostgreSQL persistence configuration.";

    public Task<IReadOnlyList<RosterTemplateSummary>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RosterTemplateSummary>>([Summary()]);

    public Task<RosterTemplateDetail?> GetAsync(Guid templateId, CancellationToken cancellationToken) =>
        Task.FromResult<RosterTemplateDetail?>(templateId == DefaultId ? Detail() : null);

    public Task<RosterTemplateDetail?> GetActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult<RosterTemplateDetail?>(Detail());

    public Task<RosterTemplateSummary> CreateAsync(CreateRosterTemplateRequest request, CancellationToken cancellationToken) =>
        throw new ConflictAppException(RequiresDatabase);

    public Task<RosterTemplateSummary> ActivateAsync(Guid templateId, CancellationToken cancellationToken) =>
        throw new ConflictAppException(RequiresDatabase);

    private static RosterTemplateSummary Summary() => new(
        DefaultId, DefaultRosterTemplate.TemplateName, true, DefaultRosterTemplate.PickTimerSeconds,
        DefaultRosterTemplate.Slots().Count, DateTimeOffset.UnixEpoch);

    private static RosterTemplateDetail Detail() => new(
        Summary(),
        DefaultRosterTemplate.Slots()
            .Select(slot => new RosterSlotDto(slot.Order, slot.SlotType.ToString(), slot.Position, slot.Label))
            .ToArray());
}

/// <summary>
/// Clubs for the in-memory foundation: derived from the bundled dataset. Five-star eligibility is
/// admin-curated and requires the database, so eligibility is always false here.
/// </summary>
public sealed class InMemoryClubDirectoryService(IBundledDataset bundled) : IClubDirectoryService
{
    private const string RequiresDatabase = "Curating five-star clubs requires the PostgreSQL persistence configuration.";

    public Task<IReadOnlyList<ClubDto>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        var term = search?.Trim();
        var clubs = bundled.Load()
            .Where(row => !string.IsNullOrWhiteSpace(row.Club))
            .GroupBy(row => row.Club!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ClubDto(
                DeterministicId(group.Key),
                group.Key,
                group.Select(row => row.League ?? string.Empty).FirstOrDefault(league => league.Length > 0) ?? string.Empty,
                false))
            .Where(club => string.IsNullOrEmpty(term)
                || club.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || club.League.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(club => club.Name)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ClubDto>>(clubs);
    }

    public Task<IReadOnlyList<ClubDto>> ListEligibleAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ClubDto>>([]);

    public Task<ClubDto> SetFiveStarEligibilityAsync(Guid clubId, bool eligible, CancellationToken cancellationToken) =>
        throw new ConflictAppException(RequiresDatabase);

    // A stable id per club name so the read-only list has consistent identifiers within a process.
    private static Guid DeterministicId(string name)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(name.ToUpperInvariant()));
        return new Guid(bytes);
    }
}
