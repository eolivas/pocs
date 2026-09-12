using LoanManagement.Shared.Kernel;

namespace LoanManagement.Loans.Domain.Events;

/// <summary>
/// The loan has been set up for servicing (schedule ready, payments can be applied).
/// This is what makes the borrower's Portal experience available.
/// </summary>
public sealed record LoanBoardedEvent(Guid LoanId, Guid ApplicationId) : DomainEvent;
