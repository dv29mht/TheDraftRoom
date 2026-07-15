using FcDraft.Application.Common.Interfaces;
using FcDraft.Infrastructure.Auth;
using FcDraft.Infrastructure.Email;
using FcDraft.Infrastructure.Live;
using FcDraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

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

        AddSqlPersistence(services, configuration, NormalizePostgresConnectionString(connectionString));
        return services;
    }

    /// <summary>
    /// Accepts either an ADO.NET/Npgsql key-value connection string or a libpq URI
    /// (<c>postgres://</c> / <c>postgresql://</c>) as handed out by Neon and most managed Postgres
    /// providers, returning the key-value form Npgsql expects. Passing the URI form straight to
    /// Npgsql would throw, so normalizing here lets an operator paste the provider's string verbatim.
    /// </summary>
    internal static string NormalizePostgresConnectionString(string connectionString)
    {
        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var uri = new Uri(connectionString);
        var credentials = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null,
        };

        foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = parameter.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0]);
            var value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty;

            // Map the SSL requirement (Neon requires it); other libpq params are left to Npgsql defaults.
            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<SslMode>(value, ignoreCase: true, out var sslMode))
            {
                builder.SslMode = sslMode;
            }
        }

        return builder.ConnectionString;
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
