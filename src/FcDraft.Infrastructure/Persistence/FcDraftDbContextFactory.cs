using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used only by the EF Core CLI (for example
/// <c>dotnet ef migrations add</c>). Adding a migration is an offline operation, so the
/// placeholder connection string below is never used to reach a real server and is not a
/// secret. At runtime the context is configured from the real connection string in
/// <see cref="DependencyInjection.AddInfrastructure"/>.
/// </summary>
public sealed class FcDraftDbContextFactory : IDesignTimeDbContextFactory<FcDraftDbContext>
{
    public FcDraftDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DRAFTROOM_DESIGN_CONNECTION")
            ?? "Host=localhost;Database=draftroom;Username=postgres;Password=postgres;";

        var options = new DbContextOptionsBuilder<FcDraftDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new FcDraftDbContext(options);
    }
}
