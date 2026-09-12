using Microsoft.EntityFrameworkCore;

namespace LoanManagement.Contracts.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Contracts service. Owns <c>ContractsDb</c>.
/// Entity sets and configurations are added in a later deep-dive. Database-per-service:
/// this context never touches another service's schema.
/// </summary>
public sealed class ContractsDbContext(DbContextOptions<ContractsDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContractsDbContext).Assembly);
    }
}
