using FcDraft.Application.Common.Interfaces;
using FcDraft.Infrastructure.Auth;
using FcDraft.Infrastructure.Email;
using FcDraft.Infrastructure.Live;
using FcDraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FcDraft.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Configuration key holding the PostgreSQL connection string.</summary>
    public const string ConnectionStringName = "DraftRoom";

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

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IAdminNotificationService, InMemoryAdminNotificationService>();
        services.AddSingleton<IDraftRoomService, InMemoryDraftRoomService>();

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // No database configured: run the in-memory foundation so a fresh clone and the
            // hermetic test suite work without PostgreSQL. Supplying a connection string switches
            // the whole identity store onto PostgreSQL persistence below.
            services.AddSingleton<IIdentityService, InMemoryIdentityService>();
            return services;
        }

        AddSqlPersistence(services, configuration, connectionString);
        return services;
    }

    private static void AddSqlPersistence(
        IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddDbContext<FcDraftDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IIdentityService, EfIdentityService>();
        services.AddScoped<ITransactionRunner, EfTransactionRunner>();
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
    }
}
