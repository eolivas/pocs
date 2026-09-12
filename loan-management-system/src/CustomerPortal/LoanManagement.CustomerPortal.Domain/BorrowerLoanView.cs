namespace LoanManagement.CustomerPortal.Domain;

/// <summary>
/// A local read-model projection of a borrower's loan, built from events published by
/// the Loans service (<c>LoanFunded</c>, <c>LoanBoarded</c>, ...). The Customer Portal
/// serves borrower reads from THIS local table, so the portal stays available even when
/// the Loans/Contracts services are down — the read path has no runtime dependency on them.
///
/// This is a projection, not a rich aggregate: it has no invariants, only last-writer state
/// that consumers upsert. Keyed by LoanId.
/// </summary>
public sealed class BorrowerLoanView
{
    public Guid LoanId { get; set; }
    public Guid ContractId { get; set; }
    public Guid ApplicationId { get; set; }

    /// <summary>Latest known loan status (e.g. Funded, Boarded).</summary>
    public string Status { get; set; } = "Unknown";

    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    public DateTime? FundedAt { get; set; }
    public DateTime? BoardedAt { get; set; }

    /// <summary>When this projection row was last updated (for staleness diagnostics).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
