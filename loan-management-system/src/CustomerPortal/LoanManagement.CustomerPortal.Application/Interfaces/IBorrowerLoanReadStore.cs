using LoanManagement.CustomerPortal.Application.DTOs;

namespace LoanManagement.CustomerPortal.Application.Interfaces;

/// <summary>
/// Reads the Customer Portal's LOCAL loan projection. This is the availability boundary:
/// the read path touches only PortalDb, so the portal keeps serving borrower reads even
/// when the Loans / Contracts services are unavailable.
/// </summary>
public interface IBorrowerLoanReadStore
{
    Task<IReadOnlyList<BorrowerLoanDto>> GetByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default);

    Task<BorrowerLoanDto?> GetByLoanAsync(Guid loanId, CancellationToken cancellationToken = default);
}
