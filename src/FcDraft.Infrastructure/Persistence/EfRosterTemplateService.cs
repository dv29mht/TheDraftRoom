using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Rosters;
using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// Database-backed roster template management (PR-09). Templates are versioned and retained; exactly
/// one is active. A draft snapshots the active template's ordered slots at start (PR-10), so edits
/// here never change an in-progress draft.
/// </summary>
public sealed class EfRosterTemplateService(FcDraftDbContext dbContext) : IRosterTemplateService
{
    public async Task<IReadOnlyList<RosterTemplateSummary>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.RosterTemplates
            .AsNoTracking()
            .OrderByDescending(template => template.IsActive)
            .ThenByDescending(template => template.CreatedAt)
            .Select(template => new RosterTemplateSummary(
                template.Id, template.Name, template.IsActive, template.PickTimerSeconds,
                template.Slots.Count, template.CreatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<RosterTemplateDetail?> GetAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var template = await dbContext.RosterTemplates
            .AsNoTracking()
            .Include(candidate => candidate.Slots)
            .FirstOrDefaultAsync(candidate => candidate.Id == templateId, cancellationToken);
        return template is null ? null : ToDetail(template);
    }

    public async Task<RosterTemplateDetail?> GetActiveAsync(CancellationToken cancellationToken)
    {
        var template = await dbContext.RosterTemplates
            .AsNoTracking()
            .Include(candidate => candidate.Slots)
            .FirstOrDefaultAsync(candidate => candidate.IsActive, cancellationToken);
        return template is null ? null : ToDetail(template);
    }

    public async Task<RosterTemplateSummary> CreateAsync(
        CreateRosterTemplateRequest request, CancellationToken cancellationToken)
    {
        if (request.Slots is null || request.Slots.Count == 0)
        {
            throw new ValidationAppException(new Dictionary<string, string[]>
            {
                ["slots"] = ["A template must define at least one slot."],
            });
        }

        var template = new RosterTemplate
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? "Untitled template" : request.Name.Trim(),
            PickTimerSeconds = request.PickTimerSeconds is >= 30 and <= 600 ? request.PickTimerSeconds : 120,
            IsActive = false,
        };

        foreach (var slot in request.Slots.OrderBy(slot => slot.Order))
        {
            if (!Enum.TryParse<RosterSlotType>(slot.SlotType, ignoreCase: true, out var slotType))
            {
                throw new ValidationAppException(new Dictionary<string, string[]>
                {
                    ["slotType"] = [$"Unknown slot type '{slot.SlotType}'."],
                });
            }

            template.Slots.Add(new RosterSlot
            {
                TemplateId = template.Id,
                Order = slot.Order,
                SlotType = slotType,
                Position = slotType == RosterSlotType.StartingPosition ? slot.Position?.Trim().ToUpperInvariant() : null,
                Label = string.IsNullOrWhiteSpace(slot.Label) ? slotType.ToString() : slot.Label.Trim(),
            });
        }

        dbContext.RosterTemplates.Add(template);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSummary(template);
    }

    public async Task<RosterTemplateSummary> ActivateAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var template = await dbContext.RosterTemplates
            .FirstOrDefaultAsync(candidate => candidate.Id == templateId, cancellationToken)
            ?? throw new KeyNotFoundException("Roster template not found.");

        var others = await dbContext.RosterTemplates
            .Where(candidate => candidate.IsActive && candidate.Id != templateId)
            .ToListAsync(cancellationToken);
        foreach (var other in others)
        {
            other.IsActive = false;
        }

        template.IsActive = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSummary(template);
    }

    private static RosterTemplateSummary ToSummary(RosterTemplate template) => new(
        template.Id, template.Name, template.IsActive, template.PickTimerSeconds, template.Slots.Count, template.CreatedAt);

    private static RosterTemplateDetail ToDetail(RosterTemplate template) => new(
        ToSummary(template),
        template.Slots
            .OrderBy(slot => slot.Order)
            .Select(slot => new RosterSlotDto(slot.Order, slot.SlotType.ToString(), slot.Position, slot.Label))
            .ToArray());
}
