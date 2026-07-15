using System.Reflection;
using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// The application's PostgreSQL database context. Tables and columns are mapped explicitly to
/// snake_case through the configurations in <c>Persistence/Configurations</c>; the schema is
/// owned entirely by migrations so a clean database can be created without manual DDL.
/// </summary>
public sealed class FcDraftDbContext(DbContextOptions<FcDraftDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<PlatformMetadata> PlatformMetadata => Set<PlatformMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
