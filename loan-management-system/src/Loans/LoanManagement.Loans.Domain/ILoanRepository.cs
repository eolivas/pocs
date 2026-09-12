namespace LoanManagement.Loans.Domain;

/// <summary>
/// Persistence contract for the <see cref="Loan"/> aggregate. Implemented in
/// Infrastructure. Lookups by contract support idempotent funding (find-or-create).
/// </summary>
public interface ILoanRepository
{
    Task<Loan?> GetByIdAsync(LoanId id, CancellationToken cancellationToken = default);

    Task<Loan?> GetByContractIdAsync(ContractId contractId, CancellationToken cancellationToken = default);

    Task AddAsync(Loan loan, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
