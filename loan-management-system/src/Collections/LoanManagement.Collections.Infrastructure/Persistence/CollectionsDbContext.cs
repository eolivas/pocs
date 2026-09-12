using Microsoft.EntityFrameworkCore;

namespace LoanManagement.Collections.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Collections / Recovery service. Owns <c>CollectionsDb</c>.
/// Entity sets and configurations are added in a later deep-dive. Database-per-service:
/// this context never touches another service's schema.
/// </summary>
public sealed class CollectionsDbContext(DbContextOptions<CollectionsDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // modelBuilder.ApplyConfigurationsFromAssembly(typeof(CollectionsDbContext).Assembly);
    }
}
