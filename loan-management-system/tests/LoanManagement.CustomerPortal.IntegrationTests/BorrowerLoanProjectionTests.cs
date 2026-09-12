using FluentAssertions;
using LoanManagement.CustomerPortal.Application.IntegrationEvents;
using LoanManagement.CustomerPortal.Infrastructure.Persistence;
using LoanManagement.CustomerPortal.Infrastructure.Projections;
using Microsoft.EntityFrameworkCore;

namespace LoanManagement.CustomerPortal.IntegrationTests;

/// <summary>
/// Proves the Portal read-model projection is available and correct under at-least-once
/// AND out-of-order delivery — the read side that keeps the portal up during outages.
/// </summary>
public class BorrowerLoanProjectionTests
{
    private const string FundedConsumer = "Portal.LoanFunded";
    private const string BoardedConsumer = "Portal.LoanBoarded";

    private static BorrowerLoanProjection NewProjection(CustomerPortalDbContext db)
        => new(db, new DeduplicationStore(db));

    [Fact]
    public async Task Funded_Then_Boarded_ProducesBoardedView()
    {
        using var sut = new SqlitePortalDb();
        var loanId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        await NewProjection(sut.Context).ApplyFundedAsync(
            new LoanFundedIntegrationEvent(Guid.NewGuid(), loanId, Guid.NewGuid(), appId, 5000m, "USD", DateTime.UtcNow),
            FundedConsumer);
        await NewProjection(sut.NewContext()).ApplyBoardedAsync(
            new LoanBoardedIntegrationEvent(Guid.NewGuid(), loanId, appId, DateTime.UtcNow),
            BoardedConsumer);

        await using var read = sut.NewContext();
        var view = await read.BorrowerLoans.SingleAsync(v => v.LoanId == loanId);
        view.Status.Should().Be("Boarded");
        view.Amount.Should().Be(5000m);
    }

    [Fact]
    public async Task SameFundedEventTwice_IsIdempotent()
    {
        using var sut = new SqlitePortalDb();
        var loanId = Guid.NewGuid();
        var evt = new LoanFundedIntegrationEvent(Guid.NewGuid(), loanId, Guid.NewGuid(), Guid.NewGuid(), 5000m, "USD", DateTime.UtcNow);

        await NewProjection(sut.Context).ApplyFundedAsync(evt, FundedConsumer);
        await NewProjection(sut.NewContext()).ApplyFundedAsync(evt, FundedConsumer); // duplicate

        await using var read = sut.NewContext();
        (await read.BorrowerLoans.CountAsync(v => v.LoanId == loanId)).Should().Be(1);
    }

    [Fact]
    public async Task OutOfOrder_BoardedBeforeFunded_StatusStaysBoarded_AndBackfills()
    {
        using var sut = new SqlitePortalDb();
        var loanId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        // Boarded arrives FIRST (out of order): creates the row as Boarded.
        await NewProjection(sut.Context).ApplyBoardedAsync(
            new LoanBoardedIntegrationEvent(Guid.NewGuid(), loanId, appId, DateTime.UtcNow),
            BoardedConsumer);

        // Funded arrives LATER: backfills amount/contract but must NOT downgrade to Funded.
        await NewProjection(sut.NewContext()).ApplyFundedAsync(
            new LoanFundedIntegrationEvent(Guid.NewGuid(), loanId, Guid.NewGuid(), appId, 7500m, "USD", DateTime.UtcNow),
            FundedConsumer);

        await using var read = sut.NewContext();
        var view = await read.BorrowerLoans.SingleAsync(v => v.LoanId == loanId);
        view.Status.Should().Be("Boarded", "status must not regress when Funded arrives after Boarded");
        view.Amount.Should().Be(7500m, "the later Funded event still backfills the amount");
    }
}
