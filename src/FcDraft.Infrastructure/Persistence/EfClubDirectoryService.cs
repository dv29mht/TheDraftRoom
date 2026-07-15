using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Rosters;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// Reads clubs from the active dataset version and curates five-star Kick Off eligibility (PR-09).
/// Only clubs from the active dataset are returned, so eligible clubs always match the pinned dataset.
/// </summary>
public sealed class EfClubDirectoryService(FcDraftDbContext dbContext) : IClubDirectoryService
{
    public async Task<IReadOnlyList<ClubDto>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        var versionId = await ActiveVersionIdAsync(cancellationToken);
        if (versionId is null)
        {
            return [];
        }

        var query = dbContext.Clubs.AsNoTracking().Where(club => club.DatasetVersionId == versionId);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{UserDirectory.EscapeLike(term)}%";
            query = query.Where(club =>
                EF.Functions.ILike(club.Name, pattern, UserDirectory.LikeEscape)
                || EF.Functions.ILike(club.League, pattern, UserDirectory.LikeEscape));
        }

        return await query
            .OrderBy(club => club.Name)
            .Select(club => new ClubDto(club.Id, club.Name, club.League, club.IsFiveStarEligible))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClubDto>> ListEligibleAsync(CancellationToken cancellationToken)
    {
        var versionId = await ActiveVersionIdAsync(cancellationToken);
        if (versionId is null)
        {
            return [];
        }

        return await dbContext.Clubs.AsNoTracking()
            .Where(club => club.DatasetVersionId == versionId && club.IsFiveStarEligible)
            .OrderBy(club => club.Name)
            .Select(club => new ClubDto(club.Id, club.Name, club.League, club.IsFiveStarEligible))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<ClubDto> SetFiveStarEligibilityAsync(Guid clubId, bool eligible, CancellationToken cancellationToken)
    {
        var club = await dbContext.Clubs.FirstOrDefaultAsync(candidate => candidate.Id == clubId, cancellationToken)
            ?? throw new KeyNotFoundException("Club not found.");
        club.IsFiveStarEligible = eligible;
        club.StarRating = eligible ? 5 : club.StarRating;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ClubDto(club.Id, club.Name, club.League, club.IsFiveStarEligible);
    }

    private async Task<Guid?> ActiveVersionIdAsync(CancellationToken cancellationToken)
    {
        var version = await dbContext.PlayerDatasetVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Status == DatasetVersionStatus.Active, cancellationToken);
        return version?.Id;
    }
}
