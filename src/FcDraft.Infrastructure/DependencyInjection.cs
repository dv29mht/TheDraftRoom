using FcDraft.Application.Common.Interfaces;
using FcDraft.Infrastructure.Auth;
using FcDraft.Infrastructure.Datasets;
using FcDraft.Infrastructure.Drafts;
using FcDraft.Infrastructure.Email;
using FcDraft.Infrastructure.Live;
using FcDraft.Infrastructure.Persistence;
using FcDraft.Infrastructure.Rosters;
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
        services.AddHttpClient<IPasswordResetEmailSender, BrevoPasswordResetEmailSender>(client =>
        {
            client.BaseAddress = new Uri("https://api.brevo.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IAdminNotificationService, InMemoryAdminNotificationService>();

        // Security primitives shared by both stores: BCrypt hashing (PRD §12.3) and the failed-login
        // throttle (per-process; TimeProvider makes its lockout window deterministic in tests).
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ILoginThrottle, LoginThrottle>();

        // The unbiased spinner shuffle (PR-13). Injected like TimeProvider so tests are deterministic; both
        // the in-memory foundation and the SQL branch resolve the same Fisher–Yates implementation.
        services.AddSingleton<IShuffler>(_ => new FisherYatesShuffler(Random.Shared));

        // The packaged FC 26 dataset (embedded resource) backs both dev seeding and "import bundled".
        services.AddSingleton<IBundledDataset, BundledPlayerDataset>();

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // No database configured: run the in-memory foundation so a fresh clone and the
            // hermetic test suite work without PostgreSQL. Supplying a connection string switches
            // the whole identity store onto PostgreSQL persistence below.
            services.AddSingleton<IIdentityService, InMemoryIdentityService>();
            services.AddSingleton<ISecurityAuditService, InMemorySecurityAuditService>();
            // No durable outbox without a database: deliver email inline and report an empty outbox.
            services.AddSingleton<IEmailQueue, DirectEmailQueue>();
            services.AddSingleton<IEmailOutboxReader, EmptyEmailOutboxReader>();
            // Dataset versioning needs the database; expose the bundled snapshot read-only, and serve
            // the explorer from that bundled snapshot.
            services.AddSingleton<IDatasetAdminService, InMemoryDatasetAdminService>();
            services.AddSingleton<IPlayerQueryService, InMemoryPlayerQueryService>();
            // Roster templates + club eligibility: read-only defaults without a database.
            services.AddSingleton<IRosterTemplateService, InMemoryRosterTemplateService>();
            services.AddSingleton<IClubDirectoryService, InMemoryClubDirectoryService>();
            // Persistent draft aggregate (PR-10): in-memory store + a pass-through transaction runner
            // (the SQL branch registers EfTransactionRunner) so the draft command handlers resolve here too.
            services.AddSingleton<IDraftStore, InMemoryDraftStore>();
            services.AddSingleton<ITransactionRunner, InMemoryTransactionRunner>();
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
        services.AddScoped<ISecurityAuditService, EfSecurityAuditService>();
        services.AddScoped<ITransactionRunner, EfTransactionRunner>();
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();

        // Durable email outbox: enqueue in the account transaction, deliver in the background.
        services.AddScoped<IEmailQueue, OutboxEmailQueue>();
        services.AddScoped<IEmailOutboxProcessor, EmailOutboxProcessor>();
        services.AddScoped<IEmailOutboxReader, EfEmailOutboxReader>();
        services.AddHostedService<EmailOutboxWorker>();

        // Versioned dataset import/activation (PR-07) and the read side for the explorer (PR-08).
        services.AddScoped<IDatasetAdminService, EfDatasetAdminService>();
        services.AddScoped<IPlayerQueryService, EfPlayerQueryService>();

        // Roster templates + five-star club eligibility (PR-09).
        services.AddScoped<IRosterTemplateService, EfRosterTemplateService>();
        services.AddScoped<IClubDirectoryService, EfClubDirectoryService>();

        // Persistent draft aggregate + append-only event history (PR-10).
        services.AddScoped<IDraftStore, EfDraftStore>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
    }
}
