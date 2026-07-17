using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Rosters;
using FcDraft.Infrastructure.Datasets;

namespace FcDraft.Infrastructure.Rosters;

/// <summary>
/// Roster templates for the in-memory foundation: the full <see cref="FormationCatalog"/> of FIFA
/// formations is exposed read-only so a host can pick any formation per lobby (PR-11), and the active
/// default can be switched (held in this singleton). Creating <em>custom</em> templates still requires
/// the database — the catalogue is fixed.
/// </summary>
public sealed class InMemoryRosterTemplateService : IRosterTemplateService
{
    private const string CustomRequiresDatabase = "Creating custom roster templates requires the PostgreSQL persistence configuration; pick one of the built-in formations instead.";

    // The active formation is a single reference: reference assignment is atomic, and `volatile`
    // publishes a switch to concurrent readers without a lock (this service is a singleton). A Guid
    // field would need a lock on every read to avoid a torn/stale read.
    private volatile FormationCatalog.Formation _active = FormationCatalog.Default;

    public Task<IReadOnlyList<RosterTemplateSummary>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RosterTemplateSummary>>(
            FormationCatalog.All.Select(Summary).ToArray());

    public Task<RosterTemplateDetail?> GetAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var formation = FormationCatalog.Find(templateId);
        return Task.FromResult(formation is null ? null : Detail(formation));
    }

    public Task<RosterTemplateDetail?> GetActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult<RosterTemplateDetail?>(Detail(_active));

    public Task<RosterTemplateSummary> CreateAsync(CreateRosterTemplateRequest request, CancellationToken cancellationToken) =>
        throw new ConflictAppException(CustomRequiresDatabase);

    public Task<RosterTemplateSummary> ActivateAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var formation = FormationCatalog.Find(templateId)
            ?? throw new ConflictAppException("That formation is not in the catalogue.");
        _active = formation;
        return Task.FromResult(Summary(formation));
    }

    private RosterTemplateSummary Summary(FormationCatalog.Formation formation) => new(
        formation.Id, formation.Name, formation.Id == _active.Id, FormationCatalog.PickTimerSeconds,
        FormationCatalog.Slots(formation).Count, DateTimeOffset.UnixEpoch);

    private RosterTemplateDetail Detail(FormationCatalog.Formation formation) => new(
        Summary(formation),
        FormationCatalog.Slots(formation)
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
                InMemoryClubId.For(group.Key),
                group.Key,
                group.Select(row => row.League ?? string.Empty).FirstOrDefault(league => league.Length > 0) ?? string.Empty,
                FiveStarClubs.Contains(group.Key)))
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
