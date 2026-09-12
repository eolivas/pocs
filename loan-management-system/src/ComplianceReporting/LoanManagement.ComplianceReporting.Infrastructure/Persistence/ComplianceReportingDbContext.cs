using Microsoft.EntityFrameworkCore;

namespace LoanManagement.ComplianceReporting.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Compliance & Reporting service. Owns <c>ComplianceReportingDb</c>.
/// Entity sets and configurations are added in a later deep-dive. Database-per-service:
/// this context never touches another service's schema.
/// </summary>
public sealed class ComplianceReportingDbContext(DbContextOptions<ComplianceReportingDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // modelBuilder.ApplyConfigurationsFromAssembly(typeof(ComplianceReportingDbContext).Assembly);
    }
}
