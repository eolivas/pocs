using Microsoft.EntityFrameworkCore;

namespace LoanManagement.CreditEngine.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Credit Engine service. Owns <c>CreditDb</c>.
/// Entity sets and configurations are added in a later deep-dive. Database-per-service:
/// this context never touches another service's schema.
/// </summary>
public sealed class CreditEngineDbContext(DbContextOptions<CreditEngineDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // modelBuilder.ApplyConfigurationsFromAssembly(typeof(CreditEngineDbContext).Assembly);
    }
}
