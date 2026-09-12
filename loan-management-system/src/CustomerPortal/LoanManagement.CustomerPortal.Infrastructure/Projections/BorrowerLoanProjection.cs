using LoanManagement.CustomerPortal.Application.IntegrationEvents;
using LoanManagement.CustomerPortal.Domain;
using LoanManagement.CustomerPortal.Infrastructure.Persistence;
using LoanManagement.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LoanManagement.CustomerPortal.Infrastructure.Projections;

/// <summary>
/// Builds the local <see cref="BorrowerLoanView"/> read model from Loans events. Designed for
/// at-least-once AND out-of-order delivery:
///   - Idempotent: a dedup store skips events already applied by this consumer.
///   - Create-or-update: a row is created on whichever event arrives first (so
///     <c>LoanBoarded</c> can arrive before <c>LoanFunded</c>).
///   - Status never regresses: a monotonic precedence (Boarded &gt; Funded &gt; Unknown)
///     means a late <c>LoanFunded</c> after <c>LoanBoarded</c> won't downgrade the status.
/// Each apply commits the projection change and the dedup mark in one transaction.
/// </summary>
public sealed class BorrowerLoanProjection(CustomerPortalDbContext db, IDeduplicationStore dedup)
{
    private static readonly Dictionary<string, int> StatusRank = new()
    {
        ["Unknown"] = 0,
        ["Funded"] = 1,
        ["Boarded"] = 2,
    };

    public async Task ApplyFundedAsync(LoanFundedIntegrationEvent e, string consumer, CancellationToken ct = default)
    {
        if (await dedup.HasBeenProcessedAsync(e.EventId, consumer, ct))
            return;

        var view = await db.BorrowerLoans.FirstOrDefaultAsync(v => v.LoanId == e.LoanId, ct);
        if (view is null)
        {
            view = new BorrowerLoanView { LoanId = e.LoanId };
            db.BorrowerLoans.Add(view);
        }

        // Facts that funding establishes, regardless of arrival order.
        view.ContractId = e.ContractId;
        view.ApplicationId = e.ApplicationId;
        view.Amount = e.Amount;
        view.Currency = e.Currency;
        view.FundedAt = e.FundedAt;
        view.Status = HigherStatus(view.Status, "Funded");
        view.UpdatedAt = DateTime.UtcNow;

        await dedup.MarkProcessedAsync(e.EventId, consumer, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task ApplyBoardedAsync(LoanBoardedIntegrationEvent e, string consumer, CancellationToken ct = default)
    {
        if (await dedup.HasBeenProcessedAsync(e.EventId, consumer, ct))
            return;

        var view = await db.BorrowerLoans.FirstOrDefaultAsync(v => v.LoanId == e.LoanId, ct);
        if (view is null)
        {
            // Out-of-order: boarded arrived before funded. Create the row now; the later
            // LoanFunded will backfill amount/contract/fundedAt without downgrading status.
            view = new BorrowerLoanView { LoanId = e.LoanId, ApplicationId = e.ApplicationId };
            db.BorrowerLoans.Add(view);
        }

        view.BoardedAt = e.BoardedAt;
        view.Status = HigherStatus(view.Status, "Boarded");
        view.UpdatedAt = DateTime.UtcNow;

        await dedup.MarkProcessedAsync(e.EventId, consumer, ct);
        await db.SaveChangesAsync(ct);
    }

    private static string HigherStatus(string current, string incoming)
    {
        var c = StatusRank.GetValueOrDefault(current, 0);
        var i = StatusRank.GetValueOrDefault(incoming, 0);
        return i > c ? incoming : current;
    }
}
