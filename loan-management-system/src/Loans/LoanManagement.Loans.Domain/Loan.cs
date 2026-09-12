using LoanManagement.Loans.Domain.Events;
using LoanManagement.Loans.Domain.Exceptions;
using LoanManagement.Loans.Domain.ValueObjects;
using LoanManagement.Shared.Kernel;

namespace LoanManagement.Loans.Domain;

/// <summary>
/// The loan servicing aggregate. Owns the funding → boarding lifecycle. All state
/// transitions are guarded by invariants and are idempotent where it matters, so
/// that at-least-once event delivery cannot fund or board twice.
/// </summary>
public sealed class Loan : AggregateRoot<LoanId>
{
    public ContractId ContractId { get; private init; }
    public ApplicationId ApplicationId { get; private init; }
    public Money Principal { get; private init; } = null!;
    public LoanStatus Status { get; private set; }
    public DateTime? FundedAt { get; private set; }
    public DateTime? BoardedAt { get; private set; }
    public string? FailureReason { get; private set; }

    // EF Core.
    private Loan() { }

    /// <summary>
    /// Creates a loan ready for funding, from an approved + signed contract. The loan
    /// begins in <see cref="LoanStatus.PendingFunding"/>; no money has moved yet.
    /// </summary>
    public static Loan CreatePendingFunding(ContractId contractId, ApplicationId applicationId, Money principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!principal.IsPositive)
            throw new LoanDomainException("A loan principal must be greater than zero.");

        return new Loan
        {
            Id = LoanId.New(),
            ContractId = contractId,
            ApplicationId = applicationId,
            Principal = principal,
            Status = LoanStatus.PendingFunding,
        };
    }

    /// <summary>
    /// Marks the loan as funded (funds disbursed). Idempotent: funding an already-funded
    /// loan is a no-op and raises no new event, so a duplicated command/event funds once.
    /// </summary>
    public void Fund()
    {
        if (Status == LoanStatus.Funded || Status == LoanStatus.Boarded)
            return; // already funded — idempotent no-op

        if (Status != LoanStatus.PendingFunding)
            throw new LoanDomainException($"Cannot fund a loan in status {Status}.");

        Status = LoanStatus.Funded;
        FundedAt = DateTime.UtcNow;

        RaiseDomainEvent(new LoanFundedEvent(
            Id.Value, ContractId.Value, ApplicationId.Value, Principal.Amount, Principal.Currency));
    }

    /// <summary>
    /// Boards the funded loan for servicing. Idempotent for an already-boarded loan.
    /// A loan can only be boarded after it has been funded.
    /// </summary>
    public void Board()
    {
        if (Status == LoanStatus.Boarded)
            return; // already boarded — idempotent no-op

        if (Status != LoanStatus.Funded)
            throw new LoanDomainException($"Cannot board a loan in status {Status}; it must be funded first.");

        Status = LoanStatus.Boarded;
        BoardedAt = DateTime.UtcNow;

        RaiseDomainEvent(new LoanBoardedEvent(Id.Value, ApplicationId.Value));
    }

    /// <summary>
    /// Compensation: marks funding/boarding as failed and emits the failure event.
    /// Terminal state; a boarded loan cannot be failed.
    /// </summary>
    public void FailFunding(string reason)
    {
        if (Status == LoanStatus.Boarded)
            throw new LoanDomainException("A boarded loan cannot be marked as funding-failed.");

        if (Status == LoanStatus.FundingFailed)
            return; // already failed — idempotent no-op

        Status = LoanStatus.FundingFailed;
        FailureReason = reason;

        RaiseDomainEvent(new LoanFundingFailedEvent(
            Id.Value, ContractId.Value, ApplicationId.Value, reason));
    }
}
