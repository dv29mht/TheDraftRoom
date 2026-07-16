using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

// Team-formation, seeding, and ready-check commands (PR-12). Each follows the PR-10/PR-11 slice shape: it
// carries the last-seen ExpectedVersion, runs read → validate → mutate → append in one ITransactionRunner
// scope, and appends exactly one DraftEvent. Host-only actions check the caller is the host or an admin; the
// seed pairing, full-assignment, and readiness rules (PRD §6.2, §9.4) are enforced here, server-side.

/// <summary>Assigns (or clears) a participant's Seed 1/Seed 2 in a 2v2 draft (host-only, team formation only).</summary>
public sealed record AssignSeedCommand(
    Guid DraftId, Guid ParticipantUserId, string? Seed, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftDetail>;

/// <summary>A team the host asks to form: a display name and the member user ids (1 for 1v1, 2 for 2v2).</summary>
public sealed record TeamFormationInput(string? Name, IReadOnlyList<Guid> MemberUserIds);

/// <summary>
/// Replaces the draft's team layout (host-only, team formation only). 1v1 auto-projects one solo team per
/// participant and ignores <see cref="Teams"/>; 2v2 pairs participants into the supplied teams, each exactly
/// one Seed 1 + one Seed 2, no participant in more than one team.
/// </summary>
public sealed record FormTeamsCommand(
    Guid DraftId, IReadOnlyList<TeamFormationInput>? Teams, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftDetail>;

/// <summary>Sets the calling participant's readiness (self-service) in team formation / ready check.</summary>
public sealed record SetReadyCommand(Guid DraftId, bool Ready, int ExpectedVersion, Guid ActorUserId) : IRequest<DraftDetail>;

/// <summary>Opens the ready check (host-only, TeamFormation → ReadyCheck) once every team is valid and everyone is assigned.</summary>
public sealed record BeginReadyCheckCommand(Guid DraftId, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftDetail>;

/// <summary>Reopens team formation to fix teams (host-only, ReadyCheck → TeamFormation), clearing readiness.</summary>
public sealed record ReopenTeamFormationCommand(Guid DraftId, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftDetail>;

public sealed class AssignSeedCommandValidator : AbstractValidator<AssignSeedCommand>
{
    public AssignSeedCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.ParticipantUserId).NotEmpty();
        RuleFor(command => command.Seed)
            .Must(seed => seed is null || Enum.TryParse<DraftSeed>(seed, ignoreCase: true, out _))
            .WithMessage("Seed must be 'Seed1', 'Seed2', or null.");
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class FormTeamsCommandValidator : AbstractValidator<FormTeamsCommand>
{
    public FormTeamsCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
        RuleForEach(command => command.Teams)
            .Must(team => team.MemberUserIds is not null && team.MemberUserIds.Count > 0)
            .When(command => command.Teams is not null)
            .WithMessage("Each team must list its members.");
    }
}

public sealed class SetReadyCommandValidator : AbstractValidator<SetReadyCommand>
{
    public SetReadyCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class BeginReadyCheckCommandValidator : AbstractValidator<BeginReadyCheckCommand>
{
    public BeginReadyCheckCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class ReopenTeamFormationCommandValidator : AbstractValidator<ReopenTeamFormationCommand>
{
    public ReopenTeamFormationCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class AssignSeedCommandHandler(
    IDraftStore drafts, IIdentityService identity, ITransactionRunner transaction)
    : IRequestHandler<AssignSeedCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(AssignSeedCommand request, CancellationToken cancellationToken)
    {
        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);
            FormationGuards.EnsureTeamFormation(draft);

            if (draft.Format != DraftFormat.TwoVsTwo)
            {
                throw FormationGuards.Validation("seed", "Seeds only apply to a 2v2 draft.");
            }
            if (draft.Participants.All(participant => participant.UserId != request.ParticipantUserId))
            {
                throw new KeyNotFoundException("That user is not a participant of this draft.");
            }

            var seed = request.Seed is null ? (DraftSeed?)null : Enum.Parse<DraftSeed>(request.Seed, ignoreCase: true);
            draft.AssignSeed(request.ParticipantUserId, seed, request.ActorUserId);
            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken);
    }
}

public sealed class FormTeamsCommandHandler(
    IDraftStore drafts, IIdentityService identity, ITransactionRunner transaction)
    : IRequestHandler<FormTeamsCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(FormTeamsCommand request, CancellationToken cancellationToken)
    {
        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);
            FormationGuards.EnsureTeamFormation(draft);

            var formed = draft.Format == DraftFormat.TwoVsTwo
                ? BuildPairedTeams(draft, request.Teams)
                : await BuildSoloTeamsAsync(draft, identity, ct);

            draft.FormTeams(formed, request.ActorUserId);
            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken);
    }

    // 1v1: one solo team per participant, named after the participant, so the host confirms rather than pairs.
    private static async Task<IReadOnlyList<FormedTeam>> BuildSoloTeamsAsync(
        Draft draft, IIdentityService identity, CancellationToken cancellationToken)
    {
        var teams = new List<FormedTeam>(draft.Participants.Count);
        foreach (var participant in draft.Participants.OrderBy(participant => participant.CreatedAt))
        {
            var account = await identity.FindByIdAsync(participant.UserId, cancellationToken);
            teams.Add(new FormedTeam(account?.DisplayName ?? $"Team {teams.Count + 1}", [participant.Id]));
        }

        return teams;
    }

    // 2v2: pair the supplied members into teams, resolving user ids to participant ids and enforcing the
    // one-Seed 1 + one-Seed 2, at-most-one-team rules server-side (PRD §6.2/§7.3).
    private static IReadOnlyList<FormedTeam> BuildPairedTeams(Draft draft, IReadOnlyList<TeamFormationInput>? inputs)
    {
        if (inputs is null || inputs.Count == 0)
        {
            throw FormationGuards.Validation("teams", "Pair the participants into 2v2 teams before continuing.");
        }
        if (inputs.Count > DraftFormation.TeamBounds(draft.Format).Max)
        {
            throw FormationGuards.Validation("teams", $"A 2v2 draft forms at most {DraftFormation.TeamBounds(draft.Format).Max} teams.");
        }

        var participantByUser = draft.Participants.ToDictionary(participant => participant.UserId);
        var seen = new HashSet<Guid>();
        var teams = new List<FormedTeam>(inputs.Count);
        var index = 1;
        foreach (var input in inputs)
        {
            var members = input.MemberUserIds ?? [];
            if (members.Count != 2 || members.Distinct().Count() != 2)
            {
                throw FormationGuards.Validation("teams", "Each 2v2 team must have two distinct participants.");
            }

            var participants = new List<DraftParticipant>(2);
            foreach (var userId in members)
            {
                if (!participantByUser.TryGetValue(userId, out var participant))
                {
                    throw FormationGuards.Validation("teams", "A team references someone who is not in this lobby.");
                }
                if (!seen.Add(userId))
                {
                    throw FormationGuards.Validation("teams", "A participant cannot be on more than one team.");
                }

                participants.Add(participant);
            }

            if (participants.Count(participant => participant.Seed == DraftSeed.Seed1) != 1
                || participants.Count(participant => participant.Seed == DraftSeed.Seed2) != 1)
            {
                throw FormationGuards.Validation("teams", "Each 2v2 team needs exactly one Seed 1 and one Seed 2.");
            }

            var name = string.IsNullOrWhiteSpace(input.Name) ? $"Team {index}" : input.Name!.Trim();
            teams.Add(new FormedTeam(name, participants.Select(participant => participant.Id).ToArray()));
            index++;
        }

        return teams;
    }
}

public sealed class SetReadyCommandHandler(
    IDraftStore drafts, IIdentityService identity, ITransactionRunner transaction)
    : IRequestHandler<SetReadyCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(SetReadyCommand request, CancellationToken cancellationToken)
    {
        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);
            if (draft.Status is not (DraftStatus.TeamFormation or DraftStatus.ReadyCheck))
            {
                throw FormationGuards.Validation("status", $"Readiness can only change during team formation (it is {draft.Status}).");
            }

            var participant = draft.Participants.FirstOrDefault(candidate => candidate.UserId == request.ActorUserId)
                ?? throw new ForbiddenAppException("You are not a participant of this draft.");

            // Toggling to the same value is a no-op so a double-tap does not conflict or append a duplicate event.
            if (participant.IsReady != request.Ready)
            {
                draft.SetReady(request.ActorUserId, request.Ready);
                await drafts.SaveChangesAsync(ct);
            }

            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken);
    }
}

public sealed class BeginReadyCheckCommandHandler(
    IDraftStore drafts, IIdentityService identity, ITransactionRunner transaction)
    : IRequestHandler<BeginReadyCheckCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(BeginReadyCheckCommand request, CancellationToken cancellationToken)
    {
        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);
            FormationGuards.EnsureTeamFormation(draft);

            var requirements = DraftFormation.Evaluate(draft);
            if (!(requirements.AllPresent && requirements.AllAssigned && requirements.TeamsValid))
            {
                throw FormationGuards.Validation("formation", requirements.BlockingReasons);
            }

            draft.BeginReadyCheck(request.ActorUserId);
            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken);
    }
}

public sealed class ReopenTeamFormationCommandHandler(
    IDraftStore drafts, IIdentityService identity, ITransactionRunner transaction)
    : IRequestHandler<ReopenTeamFormationCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(ReopenTeamFormationCommand request, CancellationToken cancellationToken)
    {
        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);
            if (draft.Status != DraftStatus.ReadyCheck)
            {
                throw FormationGuards.Validation("status", $"Team formation can only be reopened from the ready check (it is {draft.Status}).");
            }

            draft.ReopenTeamFormation(request.ActorUserId);
            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken);
    }
}

/// <summary>Shared team-formation guards for the PR-12/PR-13 handlers.</summary>
internal static class FormationGuards
{
    public static void EnsureTeamFormation(Draft draft)
    {
        if (draft.Status != DraftStatus.TeamFormation)
        {
            throw Validation("status", $"This change is only allowed during team formation (the draft is {draft.Status}).");
        }
    }

    public static ValidationAppException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    public static ValidationAppException Validation(string field, IReadOnlyList<string> messages) =>
        new(new Dictionary<string, string[]> { [field] = messages.Count == 0 ? ["This draft is not ready."] : messages.ToArray() });
}
