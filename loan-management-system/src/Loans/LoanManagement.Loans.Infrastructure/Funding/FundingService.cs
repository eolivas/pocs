using LoanManagement.Loans.Application.IntegrationEvents;
using LoanManagement.Loans.Application.Interfaces;
using LoanManagement.Loans.Domain;
using LoanManagement.Loans.Domain.Exceptions;
using LoanManagement.Loans.Domain.ValueObjects;
using LoanManagement.Loans.Infrastructure.Persistence;
using LoanManagement.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LoanManagement.Loans.Infrastructure.Funding;

/// <summary>
/// Idempotent funding logic shared by the consumer and the saga. Correctness rests on
/// three layers working together:
///   1. Deduplication store — skip an event already handled by this consumer.
///   2. Find-or-create by ContractId — one contract funds one loan (DB-unique too).
///   3. The Loan aggregate's own idempotent Fund()/Board() — no double transition.
/// The dedup mark and the state change commit in ONE transaction (SaveChanges), and the
/// LoanFunded/LoanBoarded events are drained to the outbox in that same transaction.
/// </summary>
public sealed class FundingService(
    LoansDbContext db,
    IDeduplicationStore deduplication,
    ILogger<FundingService> logger) : IFundingService
{
    private const string ConsumerName = "Loans.Funding";

    public async Task FundAsync(ContractSignedIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        if (await deduplication.HasBeenProcessedAsync(@event.EventId, ConsumerName, cancellationToken))
        {
            logger.LogInformation("ContractSigned {EventId} already processed — skipping.", @event.EventId);
            return;
        }

        var contractId = new ContractId(@event.ContractId);

        var loan = await db.Loans.FirstOrDefaultAsync(
            l => l.ContractId == contractId, cancellationToken);

        if (loan is null)
        {
            loan = Loan.CreatePendingFunding(
                contractId,
                new Domain.ApplicationId(@event.ApplicationId),
                new Money(@event.Amount, @event.Currency));
            await db.Loans.AddAsync(loan, cancellationToken);
        }

        loan.Fund(); // idempotent — no-op + no event if already funded

        await deduplication.MarkProcessedAsync(@event.EventId, ConsumerName, cancellationToken);

        // One transaction: loan state + dedup row + outbox rows (LoanFunded) commit together.
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task BoardAsync(Guid contractId, CancellationToken cancellationToken = default)
    {
        var id = new ContractId(contractId);
        var loan = await db.Loans.FirstOrDefaultAsync(l => l.ContractId == id, cancellationToken)
            ?? throw new LoanDomainException($"No loan found for contract {contractId} to board.");

        loan.Board(); // idempotent
        await db.SaveChangesAsync(cancellationToken);
    }
}
