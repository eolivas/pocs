using LoanManagement.Loans.Domain;
using Microsoft.EntityFrameworkCore;

namespace LoanManagement.Loans.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="ILoanRepository"/> over <see cref="LoansDbContext"/>.</summary>
public sealed class LoanRepository(LoansDbContext db) : ILoanRepository
{
    public Task<Loan?> GetByIdAsync(LoanId id, CancellationToken cancellationToken = default)
        => db.Loans.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public Task<Loan?> GetByContractIdAsync(ContractId contractId, CancellationToken cancellationToken = default)
        => db.Loans.FirstOrDefaultAsync(l => l.ContractId == contractId, cancellationToken);

    public async Task AddAsync(Loan loan, CancellationToken cancellationToken = default)
        => await db.Loans.AddAsync(loan, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
