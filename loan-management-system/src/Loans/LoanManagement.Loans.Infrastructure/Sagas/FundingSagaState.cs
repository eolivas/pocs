using MassTransit;

namespace LoanManagement.Loans.Infrastructure.Sagas;

/// <summary>
/// Persisted state for the funding saga instance. One instance per contract being funded.
/// The <see cref="CorrelationId"/> is derived from the contract id so all events for a
/// contract route to the same saga instance.
/// </summary>
public sealed class FundingSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }

    /// <summary>Current state name (MassTransit stores this).</summary>
    public string CurrentState { get; set; } = null!;

    public Guid ContractId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid LoanId { get; set; }

    public DateTime? FundedAt { get; set; }
    public DateTime? BoardedAt { get; set; }
    public string? FailureReason { get; set; }

    // Optimistic concurrency for the saga repository.
    public uint RowVersion { get; set; }
}
