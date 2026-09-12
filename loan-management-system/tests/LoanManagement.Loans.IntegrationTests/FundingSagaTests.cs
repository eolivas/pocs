using FluentAssertions;
using LoanManagement.Loans.Application.IntegrationEvents;
using LoanManagement.Loans.Application.Interfaces;
using LoanManagement.Loans.Domain.Events;
using LoanManagement.Loans.Infrastructure.Sagas;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.Loans.IntegrationTests;

/// <summary>
/// Exercises the funding saga's state transitions and compensation using the MassTransit
/// in-memory test harness. The saga's side effects (fund / board) are delegated to a fake
/// <see cref="IFundingService"/> so these tests focus on orchestration, not persistence —
/// the exactly-once funding behaviour is proven separately in
/// <see cref="FundingServiceIdempotencyTests"/>. Domain events (which the outbox would
/// publish in production) are published directly here to drive the saga forward.
/// </summary>
public class FundingSagaTests
{
    private sealed class FakeFundingService : IFundingService
    {
        public int FundCalls { get; private set; }
        public int BoardCalls { get; private set; }
        public Task FundAsync(ContractSignedIntegrationEvent @event, CancellationToken ct = default)
        {
            FundCalls++;
            return Task.CompletedTask;
        }
        public Task BoardAsync(Guid contractId, CancellationToken ct = default)
        {
            BoardCalls++;
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider BuildHarness(FakeFundingService funding)
        => new ServiceCollection()
            .AddSingleton<IFundingService>(funding)
            .AddMassTransitTestHarness(cfg =>
                cfg.AddSagaStateMachine<FundingStateMachine, FundingSagaState>().InMemoryRepository())
            .BuildServiceProvider(true);

    [Fact]
    public async Task HappyPath_ContractSigned_Funded_Boarded_FinalizesSaga()
    {
        var funding = new FakeFundingService();
        await using var provider = BuildHarness(funding);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var applicationId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var loanId = Guid.NewGuid();

        var sagaHarness = harness.GetSagaStateMachineHarness<FundingStateMachine, FundingSagaState>();

        // 1) Contract signed → saga starts, funds, moves to Funding.
        await harness.Bus.Publish(new ContractSignedIntegrationEvent(
            Guid.NewGuid(), contractId, applicationId, 5_000m, "USD"));
        (await harness.Consumed.Any<ContractSignedIntegrationEvent>()).Should().BeTrue();

        // 2) LoanFunded (as the outbox would publish) → saga boards, moves to Funded.
        await harness.Bus.Publish(new LoanFundedEvent(loanId, contractId, applicationId, 5_000m, "USD"));
        (await harness.Consumed.Any<LoanFundedEvent>()).Should().BeTrue();

        // 3) LoanBoarded → saga finalizes.
        await harness.Bus.Publish(new LoanBoardedEvent(loanId, applicationId));
        (await harness.Consumed.Any<LoanBoardedEvent>()).Should().BeTrue();

        funding.FundCalls.Should().Be(1);
        funding.BoardCalls.Should().Be(1);

        await harness.Stop();
    }

    [Fact]
    public async Task Compensation_FundingFailed_MovesSagaToFailed()
    {
        var funding = new FakeFundingService();
        await using var provider = BuildHarness(funding);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var applicationId = Guid.NewGuid();
        var contractId = Guid.NewGuid();

        await harness.Bus.Publish(new ContractSignedIntegrationEvent(
            Guid.NewGuid(), contractId, applicationId, 5_000m, "USD"));
        (await harness.Consumed.Any<ContractSignedIntegrationEvent>()).Should().BeTrue();

        // Funding failed → saga compensates and does NOT board.
        await harness.Bus.Publish(new LoanFundingFailedEvent(
            Guid.NewGuid(), contractId, applicationId, "disbursement declined"));
        (await harness.Consumed.Any<LoanFundingFailedEvent>()).Should().BeTrue();

        funding.BoardCalls.Should().Be(0, "a failed funding must not board the loan");

        await harness.Stop();
    }
}
