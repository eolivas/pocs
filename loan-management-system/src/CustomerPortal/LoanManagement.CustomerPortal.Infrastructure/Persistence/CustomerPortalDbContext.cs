using LoanManagement.CustomerPortal.Domain;
using Microsoft.EntityFrameworkCore;

namespace LoanManagement.CustomerPortal.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Customer Portal service. Owns <c>PortalDb</c>.
/// Holds the local read-model projection (<see cref="BorrowerLoanView"/>) that the portal
/// serves borrower reads from, plus a dedup table for idempotent event consumption.
/// Database-per-service: never touches another service's schema.
/// </summary>
public sealed class CustomerPortalDbContext(DbContextOptions<CustomerPortalDbContext> options)
    : DbContext(options)
{
    public DbSet<BorrowerLoanView> BorrowerLoans => Set<BorrowerLoanView>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CustomerPortalDbContext).Assembly);
    }
}
