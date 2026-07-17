using FcDraft.Application.Common.Interfaces;
using FcDraft.Infrastructure.Auth;
using FcDraft.Infrastructure.Datasets;
using FcDraft.Infrastructure.Drafts;
using FcDraft.Infrastructure.Email;
using FcDraft.Infrastructure.Live;
using FcDraft.Infrastructure.Observability;
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
        services.AddHttpClient<IDraftEmailSender, BrevoDraftEmailSender>(client =>
        {
            client.BaseAddress = new Uri("https://api.brevo.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient<IAnnouncementEmailSender, BrevoAnnouncementEmailSender>(client =>
        {
            client.BaseAddress = new Uri("https://api.brevo.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IAdminNotificationService, InMemoryAdminNotificationService>();

        // Bound on BOTH branches: the in-memory identity store honours Database:SeedDemoAccounts
        // (PR-23 demo players) even though the rest of the section only drives SQL persistence.
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        // Observability seams (PR-22), shared by both storage branches: the AsyncLocal correlation
        // id the API middleware assigns per request, vendor-neutral System.Diagnostics metrics, and
        // the error-monitoring hook (a structured-logging default — swap the registration to point
        // at a vendor SDK later).
        services.AddSingleton<CorrelationIdAccessor>();
        services.AddSingleton<ICorrelationIdAccessor>(sp => sp.GetRequiredService<CorrelationIdAccessor>());
        services.AddMetrics();
        services.AddSingleton<IOperationalMetrics, DraftRoomMetrics>();
        services.AddSingleton<IErrorReporter, LoggingErrorReporter>();
        // §15 product analytics (PR-23): same meter, same no-vendor-lock rule as the operational seam.
        services.AddSingleton<IProductAnalytics, DraftRoomAnalytics>();

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

        // The inline-delivery ledger DirectEmailQueue records into. Registered on BOTH branches
        // (inert under the durable outbox) because the DB test factory swaps DirectEmailQueue in on
        // the SQL branch too; only the in-memory branch also exposes it as the IEmailOutboxReader.
        services.AddSingleton<InMemoryEmailOutbox>();

        // Belt-and-braces 120s-expiry sweep (PR-16) for both storage branches; the lazy read/command-path
        // evaluation stays the authority when the single instance was scaled to zero.
        services.AddHostedService<DraftTimerSweepWorker>();

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // No database configured: run the in-memory foundation so a fresh clone and the
            // hermetic test suite work without PostgreSQL. Supplying a connection string switches
            // the whole identity store onto PostgreSQL persistence below.
            services.AddSingleton<IIdentityService, InMemoryIdentityService>();
            services.AddSingleton<ISecurityAuditService, InMemorySecurityAuditService>();
            // No durable outbox without a database: deliver email inline; the ledger records each
            // outcome so the admin delivery-visibility endpoints (PR-21 §9.8) work on this branch too.
            services.AddSingleton<IEmailOutboxReader>(provider => provider.GetRequiredService<InMemoryEmailOutbox>());
            services.AddSingleton<IEmailQueue, DirectEmailQueue>();
            // Dataset versioning needs the database; expose the bundled snapshot read-only, and serve
            // the explorer from that bundled snapshot.
            services.AddSingleton<IDatasetAdminService, InMemoryDatasetAdminService>();
            services.AddSingleton<IPlayerQueryService, InMemoryPlayerQueryService>();
            // Roster templates + club eligibility: read-only defaults without a database.
            services.AddSingleton<IRosterTemplateService, InMemoryRosterTemplateService>();
            services.AddSingleton<IClubDirectoryService, InMemoryClubDirectoryService>();
            // Draft eligibility (PR-14/PR-15): the bundled snapshot backs the club/held/position pools.
            services.AddSingleton<IDraftCatalog, InMemoryDraftCatalog>();
            // Persistent draft aggregate (PR-10): in-memory store + a pass-through transaction runner
            // (the SQL branch registers EfTransactionRunner) so the draft command handlers resolve here too.
            services.AddSingleton<IDraftStore, InMemoryDraftStore>();
            services.AddSingleton<ITransactionRunner, InMemoryTransactionRunner>();
            // Per-user notifications (PR-20): survive the process only — the SQL branch persists them.
            services.AddSingleton<IUserNotificationStore, Infrastructure.Notifications.InMemoryUserNotificationStore>();
            // Admin communications + audit views (PR-21): campaign records and the append-only
            // draft-event trail, read through the same aggregates the commands mutate.
            services.AddSingleton<IAnnouncementStore, Infrastructure.Announcements.InMemoryAnnouncementStore>();
            services.AddSingleton<IDraftEventReader, InMemoryDraftEventReader>();
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

        // Draft eligibility scoped to the pinned dataset version (PR-14/PR-15).
        services.AddScoped<IDraftCatalog, EfDraftCatalog>();

        // Persistent draft aggregate + append-only event history (PR-10).
        services.AddScoped<IDraftStore, EfDraftStore>();

        // Persistent per-user notifications (PR-20): appended inside the draft transactions, so they
        // survive restarts and never outlive a rolled-back mutation.
        services.AddScoped<IUserNotificationStore, EfUserNotificationStore>();

        // Admin communications + audit views (PR-21): append-only campaign records (committed inside
        // the announcement transaction) and read-only audit queries over the draft-event trail.
        services.AddScoped<IAnnouncementStore, EfAnnouncementStore>();
        services.AddScoped<IDraftEventReader, EfDraftEventReader>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
    }
}
