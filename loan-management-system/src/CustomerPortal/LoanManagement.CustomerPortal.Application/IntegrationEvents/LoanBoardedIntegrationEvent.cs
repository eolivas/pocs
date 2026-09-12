namespace LoanManagement.CustomerPortal.Application.IntegrationEvents;

/// <summary>
/// Inbound integration event: the Loans service reports that a loan was boarded (ready
/// for servicing). Consuming this makes the borrower's portal experience fully available.
/// May arrive before <see cref="LoanFundedIntegrationEvent"/> under out-of-order delivery,
/// so the projection consumer must create-or-update.
/// </summary>
public sealed record LoanBoardedIntegrationEvent(
    Guid EventId,
    Guid LoanId,
    Guid ApplicationId,
    DateTime BoardedAt);
