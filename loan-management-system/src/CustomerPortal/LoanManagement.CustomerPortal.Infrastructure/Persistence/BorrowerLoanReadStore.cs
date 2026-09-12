using LoanManagement.CustomerPortal.Application.DTOs;
using LoanManagement.CustomerPortal.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LoanManagement.CustomerPortal.Infrastructure.Persistence;

/// <summary>
/// Reads the local BorrowerLoanView projection. No calls to other services — this is why
/// the portal read path stays available during downstream outages.
/// </summary>
public sealed class BorrowerLoanReadStore(CustomerPortalDbContext db) : IBorrowerLoanReadStore
{
    public async Task<IReadOnlyList<BorrowerLoanDto>> GetByApplicationAsync(
        Guid applicationId, CancellationToken cancellationToken = default)
        => await db.BorrowerLoans.AsNoTracking()
            .Where(v => v.ApplicationId == applicationId)
            .Select(v => BorrowerLoanDto.From(v))
            .ToListAsync(cancellationToken);

    public async Task<BorrowerLoanDto?> GetByLoanAsync(Guid loanId, CancellationToken cancellationToken = default)
    {
        var view = await db.BorrowerLoans.AsNoTracking().FirstOrDefaultAsync(v => v.LoanId == loanId, cancellationToken);
        return view is null ? null : BorrowerLoanDto.From(view);
    }
}
