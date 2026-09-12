using LoanManagement.Loans.Application.IntegrationEvents;
using LoanManagement.Loans.Application.Interfaces;
using LoanManagement.Loans.Domain.Events;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoanManagement.Loans.Infrastructure.Sagas;

/// <summary>
/// Orchestrates the funding lifecycle: <c>ContractSigned → [fund] → LoanFunded → [board]
/// → LoanBoarded</c>, with compensation on <c>LoanFundingFailed</c>.
///
/// The saga is the single consumer of <see cref="ContractSignedIntegrationEvent"/> (so
/// funding is triggered in exactly one place). It delegates the actual state change to
/// the idempotent <see cref="IFundingService"/>, then reacts to the domain events the
/// outbox publishes. All events correlate by ApplicationId (the through-line ID present
/// on every event).
/// </summary>
public sealed class FundingStateMachine : MassTransitStateMachine<FundingSagaState>
{
    public State Funding { get; private set; } = null!;
    public State Funded { get; private set; } = null!;
    public State Boarded { get; private set; } = null!;
    public State Failed { get; private set; } = null!;

    public Event<ContractSignedIntegrationEvent> ContractSigned { get; private set; } = null!;
    public Event<LoanFundedEvent> LoanFunded { get; private set; } = null!;
    public Event<LoanBoardedEvent> LoanBoarded { get; private set; } = null!;
    public Event<LoanFundingFailedEvent> LoanFundingFailed { get; private set; } = null!;

    public FundingStateMachine(IServiceScopeFactory scopeFactory, ILogger<FundingStateMachine> logger)
    {
        InstanceState(x => x.CurrentState);

        // Correlate every event by ApplicationId — the ID present on all of them.
        Event(() => ContractSigned, x => x.CorrelateById(ctx => ctx.Message.ApplicationId));
        Event(() => LoanFunded, x => x.CorrelateById(ctx => ctx.Message.ApplicationId));
        Event(() => LoanBoarded, x => x.CorrelateById(ctx => ctx.Message.ApplicationId));
        Event(() => LoanFundingFailed, x => x.CorrelateById(ctx => ctx.Message.ApplicationId));

        Initially(
            When(ContractSigned)
                .Then(ctx =>
                {
                    ctx.Saga.ContractId = ctx.Message.ContractId;
                    ctx.Saga.ApplicationId = ctx.Message.ApplicationId;
                })
                .ThenAsync(async ctx =>
                {
                    // Idempotent funding. On success it publishes LoanFunded via the outbox.
                    using var scope = scopeFactory.CreateScope();
                    var funding = scope.ServiceProvider.GetRequiredService<IFundingService>();
                    try
                    {
                        await funding.FundAsync(ctx.Message, ctx.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Funding failed for contract {ContractId}.", ctx.Message.ContractId);
                    }
                })
                .TransitionTo(Funding));

        During(Funding,
            When(LoanFunded)
                .Then(ctx =>
                {
                    ctx.Saga.LoanId = ctx.Message.LoanId;
                    ctx.Saga.FundedAt = DateTime.UtcNow;
                })
                .ThenAsync(async ctx =>
                {
                    // Funds disbursed → board the loan for servicing (idempotent).
                    using var scope = scopeFactory.CreateScope();
                    var funding = scope.ServiceProvider.GetRequiredService<IFundingService>();
                    await funding.BoardAsync(ctx.Saga.ContractId, ctx.CancellationToken);
                })
                .TransitionTo(Funded),
            When(LoanFundingFailed)
                .Then(ctx => ctx.Saga.FailureReason = ctx.Message.Reason)
                .TransitionTo(Failed)
                .Finalize());

        During(Funded,
            When(LoanBoarded)
                .Then(ctx => ctx.Saga.BoardedAt = DateTime.UtcNow)
                .TransitionTo(Boarded)
                .Finalize());

        // Tolerate out-of-order / duplicate delivery of terminal events.
        DuringAny(
            When(LoanFunded).Then(ctx => ctx.Saga.LoanId = ctx.Message.LoanId),
            When(LoanFundingFailed)
                .Then(ctx => ctx.Saga.FailureReason = ctx.Message.Reason)
                .TransitionTo(Failed)
                .Finalize());

        SetCompletedWhenFinalized();
    }
}
