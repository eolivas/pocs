using MediatR;

namespace LoanManagement.Loans.Application.Commands;

/// <summary>
/// Direct command to fund a loan for a signed contract (e.g. an ops/manual trigger or a
/// synchronous test entry point). Idempotent: the underlying funding service ensures the
/// same contract is funded exactly once, so replaying this command is safe.
/// </summary>
public sealed record FundLoanCommand : IRequest
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public Guid ContractId { get; init; }
    public Guid ApplicationId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
}
