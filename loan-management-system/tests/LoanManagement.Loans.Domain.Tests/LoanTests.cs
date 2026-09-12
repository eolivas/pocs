using FluentAssertions;
using LoanManagement.Loans.Domain;
using LoanManagement.Loans.Domain.Events;
using LoanManagement.Loans.Domain.Exceptions;
using LoanManagement.Loans.Domain.ValueObjects;

namespace LoanManagement.Loans.Domain.Tests;

public class LoanTests
{
    private static Loan NewPendingLoan() =>
        Loan.CreatePendingFunding(
            new ContractId(Guid.NewGuid()),
            new ApplicationId(Guid.NewGuid()),
            new Money(5_000m, "USD"));

    [Fact]
    public void CreatePendingFunding_StartsInPendingFunding_WithNoEvents()
    {
        var loan = NewPendingLoan();

        loan.Status.Should().Be(LoanStatus.PendingFunding);
        loan.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void CreatePendingFunding_ZeroPrincipal_Throws()
    {
        var act = () => Loan.CreatePendingFunding(
            new ContractId(Guid.NewGuid()), new ApplicationId(Guid.NewGuid()), Money.Zero("USD"));

        act.Should().Throw<LoanDomainException>();
    }

    [Fact]
    public void Fund_FromPending_TransitionsToFunded_AndRaisesEventOnce()
    {
        var loan = NewPendingLoan();

        loan.Fund();

        loan.Status.Should().Be(LoanStatus.Funded);
        loan.FundedAt.Should().NotBeNull();
        loan.DomainEvents.OfType<LoanFundedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Fund_CalledTwice_IsIdempotent_RaisesEventOnce()
    {
        var loan = NewPendingLoan();

        loan.Fund();
        loan.Fund(); // duplicate delivery

        loan.Status.Should().Be(LoanStatus.Funded);
        loan.DomainEvents.OfType<LoanFundedEvent>().Should().ContainSingle(
            "funding must be exactly-once even under duplicate delivery");
    }

    [Fact]
    public void Board_BeforeFunding_Throws()
    {
        var loan = NewPendingLoan();

        var act = () => loan.Board();

        act.Should().Throw<LoanDomainException>();
    }

    [Fact]
    public void Board_AfterFunding_TransitionsToBoarded_AndRaisesEventOnce()
    {
        var loan = NewPendingLoan();
        loan.Fund();

        loan.Board();

        loan.Status.Should().Be(LoanStatus.Boarded);
        loan.BoardedAt.Should().NotBeNull();
        loan.DomainEvents.OfType<LoanBoardedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Board_CalledTwice_IsIdempotent_RaisesEventOnce()
    {
        var loan = NewPendingLoan();
        loan.Fund();

        loan.Board();
        loan.Board(); // duplicate delivery

        loan.DomainEvents.OfType<LoanBoardedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void FailFunding_FromPending_TransitionsToFailed_AndRaisesEvent()
    {
        var loan = NewPendingLoan();

        loan.FailFunding("disbursement declined");

        loan.Status.Should().Be(LoanStatus.FundingFailed);
        loan.FailureReason.Should().Be("disbursement declined");
        loan.DomainEvents.OfType<LoanFundingFailedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void FailFunding_AfterBoarded_Throws()
    {
        var loan = NewPendingLoan();
        loan.Fund();
        loan.Board();

        var act = () => loan.FailFunding("too late");

        act.Should().Throw<LoanDomainException>("a boarded loan cannot be failed");
    }
}
