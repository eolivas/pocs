namespace LoanManagement.Loans.Infrastructure.Persistence;

/// <summary>
/// Supplies the current request's correlation id so it can be stored on outbox
/// messages and propagated to the broker (and then to consumers) for end-to-end tracing.
/// </summary>
public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; }
}
