using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Features.Drafts;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

/// <summary>
/// Exercises the draft aggregate through MediatR against the running in-memory host. Its purpose is to
/// prove the no-database DI branch wires the new pieces together — <c>IDraftStore</c> (in-memory) and the
/// pass-through <c>ITransactionRunner</c> — so the command handlers resolve and run end to end. The lobby
/// HTTP surface arrives in PR-11; PR-10 keeps the aggregate internal.
/// </summary>
public sealed class DraftAggregateTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    private ISender Sender(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<ISender>();

    [Fact]
    public async Task Create_then_transition_persists_history_through_the_in_memory_store()
    {
        var host = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var sender = Sender(scope);

        var created = await sender.Send(new CreateDraftCommand("Friday Night", "1v1", host));
        Assert.Equal("Draft", created.Status);
        Assert.Equal(1, created.Version);

        var moved = await sender.Send(new TransitionDraftCommand(created.Id, "Lobby", "ParticipantInvited", created.Version, host));
        Assert.Equal("Lobby", moved.Status);
        Assert.Equal(2, moved.Version);

        var detail = await sender.Send(new GetDraftQuery(created.Id));
        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Events.Count);
        Assert.Equal("DraftCreated", detail.Events[0].Type);
        Assert.Equal("ParticipantInvited", detail.Events[1].Type);
    }

    [Fact]
    public async Task A_stale_version_transition_conflicts()
    {
        var host = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var sender = Sender(scope);

        var created = await sender.Send(new CreateDraftCommand("Conflict Cup", "2v2", host));
        await sender.Send(new TransitionDraftCommand(created.Id, "Lobby", "ParticipantInvited", 1, host));

        await Assert.ThrowsAsync<ConflictAppException>(() =>
            sender.Send(new TransitionDraftCommand(created.Id, "TeamFormation", "TeamsFormed", 1, host)));
    }
}
