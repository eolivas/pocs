using LoanManagement.Origination.Domain;
using Microsoft.EntityFrameworkCore;
using ApplicationId = LoanManagement.Origination.Domain.ApplicationId;

namespace LoanManagement.Origination.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="ILoanApplicationRepository"/>.</summary>
public sealed class LoanApplicationRepository(OriginationDbContext db) : ILoanApplicationRepository
{
    public Task<LoanApplication?> GetByIdAsync(ApplicationId id, CancellationToken cancellationToken = default)
        => db.Applications.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task AddAsync(LoanApplication application, CancellationToken cancellationToken = default)
        => await db.Applications.AddAsync(application, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
