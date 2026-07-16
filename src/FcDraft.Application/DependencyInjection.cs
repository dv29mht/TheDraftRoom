using FcDraft.Application.Common.Behaviors;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FcDraft.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        // Publishes one live update per accepted draft mutation (PR-17); runs after the handler's
        // transaction has committed. The notifier defaults to a no-op — the API replaces it with SignalR.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(DraftNotificationBehavior<,>));
        services.TryAddSingleton<IDraftNotifier, NullDraftNotifier>();
        // Lazy 120s-expiry evaluation (PR-16), shared by the read/command paths and the hosted sweep.
        services.AddScoped<DraftExpiryService>();
        // Participant communications for draft lifecycle moments (PR-20): notification rows + outbox
        // emails appended inside the emitting handler's transaction.
        services.AddScoped<DraftParticipantNotifier>();
        return services;
    }
}
