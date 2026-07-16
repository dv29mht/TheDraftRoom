using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// Database-backed draft aggregate store (PR-10). <see cref="FindAsync"/> loads the tracked aggregate with
/// its children so a command can mutate and persist it in one transaction. A lost race — another writer
/// moved the <c>version</c> concurrency token on, or reached the next append-only event sequence first —
/// surfaces as <see cref="ConflictAppException"/> (→409), translated here so the Application layer never
/// references EF Core or Npgsql.
/// </summary>
public sealed class EfDraftStore(FcDraftDbContext dbContext) : IDraftStore
{
    public async Task AddAsync(Draft draft, CancellationToken cancellationToken) =>
        await dbContext.Drafts.AddAsync(draft, cancellationToken);

    public async Task<Draft?> FindAsync(Guid draftId, CancellationToken cancellationToken) =>
        await dbContext.Drafts
            .Include(draft => draft.Participants)
            .Include(draft => draft.Teams).ThenInclude(team => team.Members)
            .Include(draft => draft.Slots)
            .Include(draft => draft.Picks)
            .Include(draft => draft.Events)
            .FirstOrDefaultAsync(draft => draft.Id == draftId, cancellationToken);

    public async Task<IReadOnlyList<Draft>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Drafts
            .AsNoTracking()
            .Include(draft => draft.Participants)
            .OrderByDescending(draft => draft.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListDraftIdsWithExpiredTurnsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The deadline is turn_started_at + pick_timer_seconds (a per-row interval), which does not
        // translate to SQL cleanly — so fetch the few live, clocked candidates and finish the comparison
        // in memory. Drafts sitting in PositionDraft are rare (a private app), so this stays tiny.
        var candidates = await dbContext.Drafts
            .AsNoTracking()
            .Where(draft => draft.Status == DraftStatus.PositionDraft
                && draft.PausedAt == null
                && draft.TurnStartedAt != null)
            .Select(draft => new { draft.Id, draft.TurnStartedAt, draft.PickTimerSeconds })
            .ToArrayAsync(cancellationToken);

        return candidates
            .Where(candidate => now >= candidate.TurnStartedAt!.Value.AddSeconds(candidate.PickTimerSeconds))
            .Select(candidate => candidate.Id)
            .ToArray();
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The version token's WHERE clause matched no row: another writer already advanced this draft.
            throw new ConflictAppException(
                "This draft was updated by another action. Refresh the draft and try again.");
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent writer reached the same append-only event sequence (or draft code) first; the
            // unique index rejected the loser — the same "another action won the race" conflict.
            throw new ConflictAppException(
                "This draft was updated by another action. Refresh the draft and try again.");
        }
    }
}
