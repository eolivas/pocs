using Microsoft.EntityFrameworkCore;

namespace LoanManagement.Communications.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Communications service. Owns <c>CommunicationsDb</c>.
/// Entity sets and configurations are added in a later deep-dive. Database-per-service:
/// this context never touches another service's schema.
/// </summary>
public sealed class CommunicationsDbContext(DbContextOptions<CommunicationsDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // modelBuilder.ApplyConfigurationsFromAssembly(typeof(CommunicationsDbContext).Assembly);
    }
}
