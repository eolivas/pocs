namespace LoanManagement.Origination.Domain;

/// <summary>Persistence contract for the <see cref="LoanApplication"/> aggregate.</summary>
public interface ILoanApplicationRepository
{
    Task<LoanApplication?> GetByIdAsync(ApplicationId id, CancellationToken cancellationToken = default);

    Task AddAsync(LoanApplication application, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
