using MediatR;

namespace LoanManagement.Origination.Application.Commands;

/// <summary>Submit a partner-channel loan application. Returns the new application id.</summary>
public sealed record SubmitApplicationCommand : IRequest<Guid>
{
    public Guid PartnerId { get; init; }
    public string PartnerName { get; init; } = string.Empty;
    public decimal RequestedAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public int TermMonths { get; init; }
}
