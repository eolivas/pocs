using FluentAssertions;
using LoanManagement.Loans.Application.IntegrationEvents;
using LoanManagement.Loans.Domain;
using LoanManagement.Loans.Infrastructure.Funding;
using LoanManagement.Loans.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoanManagement.Loans.IntegrationTests;

/// <summary>
/// Proves the core thesis: funding is exactly-once even under at-least-once (duplicate)
/// delivery. Uses a real SQLite-backed DbContext so the dedup store, the unique
/// ContractId index, and the outbox all behave as they would in production.
/// </summary>
public class FundingServiceIdempotencyTests
{
    private static ContractSignedIntegrationEvent NewContractSigned(Guid? eventId = null)
        => new(
            EventId: eventId ?? Guid.NewGuid(),
            ContractId: Guid.NewGuid(),
            ApplicationId: Guid.NewGuid(),
            Amount: 5_000m,
            Currency: "USD");

    private static FundingService NewService(LoansDbContext db)
        => new(db, new DeduplicationStore(db), NullLogger<FundingService>.Instance);

    [Fact]
    public async Task Fund_Once_CreatesFundedLoan_AndOneOutboxEvent()
    {
        using var sut = new SqliteLoansDb();
        var evt = NewContractSigned();

        await NewService(sut.Context).FundAsync(evt);

        await using var read = sut.NewContext();
        var loan = await read.Loans.SingleAsync(l => l.ContractId == new ContractId(evt.ContractId));
        loan.Status.Should().Be(LoanStatus.Funded);

        read.OutboxMessages.Count(m => m.Type.Contains("LoanFundedEvent")).Should().Be(1);
    }

    [Fact]
    public async Task Fund_SameEventTwice_FundsExactlyOnce()
    {
        using var sut = new SqliteLoansDb();
        var evt = NewContractSigned();

        // Duplicate delivery of the SAME event (same EventId).
        await NewService(sut.Context).FundAsync(evt);
        await NewService(sut.NewContext()).FundAsync(evt);

        await using var read = sut.NewContext();
        (await read.Loans.CountAsync(l => l.ContractId == new ContractId(evt.ContractId)))
            .Should().Be(1, "the same contract must not produce two loans");

        read.OutboxMessages.Count(m => m.Type.Contains("LoanFundedEvent"))
            .Should().Be(1, "funding must emit exactly one LoanFunded event even on duplicate delivery");
    }

    [Fact]
    public async Task Fund_TwoDifferentDeliveriesOfSameContract_StillFundsOnce()
    {
        using var sut = new SqliteLoansDb();
        var first = NewContractSigned();
        // A redelivery that shares the contract but arrives with a different event id
        // (e.g. re-published). The find-by-contract + aggregate idempotency still hold.
        var redelivery = first with { EventId = Guid.NewGuid() };

        await NewService(sut.Context).FundAsync(first);
        await NewService(sut.NewContext()).FundAsync(redelivery);

        await using var read = sut.NewContext();
        (await read.Loans.CountAsync(l => l.ContractId == new ContractId(first.ContractId))).Should().Be(1);
        read.OutboxMessages.Count(m => m.Type.Contains("LoanFundedEvent")).Should().Be(1);
    }

    [Fact]
    public async Task Board_AfterFunding_TransitionsLoanToBoarded()
    {
        using var sut = new SqliteLoansDb();
        var evt = NewContractSigned();

        await NewService(sut.Context).FundAsync(evt);
        await NewService(sut.NewContext()).BoardAsync(evt.ContractId);

        await using var read = sut.NewContext();
        var loan = await read.Loans.SingleAsync(l => l.ContractId == new ContractId(evt.ContractId));
        loan.Status.Should().Be(LoanStatus.Boarded);
        read.OutboxMessages.Count(m => m.Type.Contains("LoanBoardedEvent")).Should().Be(1);
    }
}
