using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

/// <summary>
/// Exercises the draft aggregate through MediatR against the running in-memory host. Its purpose is to
/// prove the no-database DI branch wires the new pieces together — <c>IDraftStore</c> (in-memory), the
/// pass-through <c>ITransactionRunner</c>, and the identity lookups the lobby handlers now use — so the
/// command handlers resolve and run end to end. The HTTP surface is covered by <see cref="DraftLobbyTests"/>.
/// </summary>
public sealed class DraftAggregateTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    private static ISender Sender(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<ISender>();

    private static async Task<Guid> HostIdAsync(IServiceScope scope)
    {
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var host = await identity.FindByEmailAsync(SeededAccounts.PlayerEmail, default);
        return host!.Id;
    }

    [Fact]
    public async Task Create_opens_a_lobby_and_transition_persists_history_through_the_in_memory_store()
    {
        using var scope = factory.Services.CreateScope();
        var sender = Sender(scope);
        var host = await HostIdAsync(scope);

        var created = await sender.Send(new CreateDraftCommand("Friday Night", "1v1", host));
        Assert.Equal("Lobby", created.Summary.Status);
        Assert.Equal(2, created.Summary.Version);
        Assert.Single(created.Participants); // the host

        var moved = await sender.Send(
            new TransitionDraftCommand(created.Summary.Id, "TeamFormation", "TeamsFormed", created.Summary.Version, host));
        Assert.Equal("TeamFormation", moved.Status);
        Assert.Equal(3, moved.Version);

        var detail = await sender.Send(new GetDraftQuery(created.Summary.Id));
        Assert.NotNull(detail);
        Assert.Equal(3, detail!.Events.Count);
        Assert.Equal("DraftCreated", detail.Events[0].Type);
        Assert.Equal("ParticipantJoined", detail.Events[1].Type);
        Assert.Equal("TeamsFormed", detail.Events[2].Type);
    }

    [Fact]
    public async Task A_stale_version_transition_conflicts()
    {
        using var scope = factory.Services.CreateScope();
        var sender = Sender(scope);
        var host = await HostIdAsync(scope);

        var created = await sender.Send(new CreateDraftCommand("Conflict Cup", "2v2", host));
        await sender.Send(new TransitionDraftCommand(created.Summary.Id, "TeamFormation", "TeamsFormed", 2, host));

        await Assert.ThrowsAsync<ConflictAppException>(() =>
            sender.Send(new TransitionDraftCommand(created.Summary.Id, "ReadyCheck", "ParticipantReadied", 2, host)));
    }
}
