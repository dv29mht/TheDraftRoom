using FcDraft.Application.Common.Interfaces;
using FcDraft.Infrastructure.Auth;
using FcDraft.Infrastructure.Email;
using FcDraft.Infrastructure.Live;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FcDraft.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<BrevoOptions>(configuration.GetSection(BrevoOptions.SectionName));
        services.AddHttpClient<IInvitationEmailSender, BrevoInvitationEmailSender>(client =>
        {
            client.BaseAddress = new Uri("https://api.brevo.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddSingleton<IIdentityService, InMemoryIdentityService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IAdminNotificationService, InMemoryAdminNotificationService>();
        services.AddSingleton<IDraftRoomService, InMemoryDraftRoomService>();
        return services;
    }
}
