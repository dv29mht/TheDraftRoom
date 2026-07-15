using Testcontainers.PostgreSql;
using Xunit;

namespace FcDraft.Api.DatabaseTests;

/// <summary>
/// Starts a throwaway PostgreSQL 16 container (via Testcontainers) once for the whole database test
/// collection. When Docker is not available — a plain developer machine without Docker running, for
/// example — the container fails to start and <see cref="Available"/> is false, so the tests skip
/// cleanly instead of failing. GitHub Actions ubuntu runners ship Docker, so these tests run for
/// real in CI, which is where the "clean database created exclusively from migrations" definition
/// of done is proven.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = BuildContainer();

    private static PostgreSqlContainer BuildContainer()
    {
        // The parameterless module builder is obsolete in Testcontainers 4.13; the image is still
        // pinned explicitly via WithImage, so keep that pattern and silence only this call.
#pragma warning disable CS0618
        return new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("draftroom")
            .Build();
#pragma warning restore CS0618
    }

    /// <summary>The connection string to the application database, or null if Docker is unavailable.</summary>
    public string? ConnectionString { get; private set; }

    public bool Available => ConnectionString is not null;

    public string SkipReason { get; private set; } =
        "Docker is not available, so the PostgreSQL integration tests were skipped.";

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch (Exception exception)
        {
            SkipReason = $"PostgreSQL test container could not start ({exception.GetType().Name}: {exception.Message}).";
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
