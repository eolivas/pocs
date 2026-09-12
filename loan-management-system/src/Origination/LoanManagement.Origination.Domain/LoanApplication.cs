using LoanManagement.Origination.Domain.Events;
using LoanManagement.Origination.Domain.Exceptions;
using LoanManagement.Shared.Kernel;

namespace LoanManagement.Origination.Domain;

/// <summary>
/// The loan-application aggregate. Its <see cref="Submit"/> factory is the "accept-and-queue"
/// entry point: it validates and records the application locally and raises
/// <see cref="LoanApplicationSubmittedEvent"/> (which the outbox persists in the same
/// transaction). Downstream decisioning happens asynchronously off that event, so an
/// applicant's submission is never blocked by a Credit Engine / Contracts outage.
/// </summary>
public sealed class LoanApplication : AggregateRoot<ApplicationId>
{
    public Guid PartnerId { get; private init; }
    public string PartnerName { get; private init; } = string.Empty;
    public decimal RequestedAmount { get; private init; }
    public string Currency { get; private init; } = string.Empty;
    public int TermMonths { get; private init; }
    public ApplicationStatus Status { get; private set; }
    public DateTime SubmittedAt { get; private init; }

    // EF Core.
    private LoanApplication() { }

    public static LoanApplication Submit(
        Guid partnerId, string partnerName, decimal requestedAmount, string currency, int termMonths)
    {
        if (partnerId == Guid.Empty)
            throw new ApplicationDomainException("A partner is required to submit an application.");
        if (requestedAmount <= 0)
            throw new ApplicationDomainException("Requested amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new ApplicationDomainException("Currency must be a 3-letter code.");
        if (termMonths <= 0)
            throw new ApplicationDomainException("Term must be a positive number of months.");

        var application = new LoanApplication
        {
            Id = ApplicationId.New(),
            PartnerId = partnerId,
            PartnerName = partnerName,
            RequestedAmount = requestedAmount,
            Currency = currency.ToUpperInvariant(),
            TermMonths = termMonths,
            Status = ApplicationStatus.Submitted,
            SubmittedAt = DateTime.UtcNow,
        };

        application.RaiseDomainEvent(new LoanApplicationSubmittedEvent(
            application.Id.Value, partnerId, partnerName,
            application.RequestedAmount, application.Currency, termMonths));

        return application;
    }

    /// <summary>
    /// Advances the local status when a credit decision arrives (async, from Credit Engine).
    /// Idempotent: re-applying the same transition is a no-op. Approved → Decisioned;
    /// declined → Rejected. Never regresses from a later state.
    /// </summary>
    public void MarkDecisioned(bool approved)
    {
        if (Status is ApplicationStatus.Contracted or ApplicationStatus.Rejected)
            return; // already past this point — idempotent / no regression

        Status = approved ? ApplicationStatus.Decisioned : ApplicationStatus.Rejected;
    }

    /// <summary>Advances the local status when the contract is signed. Idempotent.</summary>
    public void MarkContracted()
    {
        if (Status == ApplicationStatus.Rejected)
            throw new ApplicationDomainException("A rejected application cannot be contracted.");

        Status = ApplicationStatus.Contracted;
    }
}
