using LoanManagement.Origination.Domain;

namespace LoanManagement.Origination.Application.DTOs;

/// <summary>Local application status read model, served from OriginationDb.</summary>
public sealed record ApplicationStatusDto(
    Guid ApplicationId,
    Guid PartnerId,
    string PartnerName,
    string Status,
    decimal RequestedAmount,
    string Currency,
    int TermMonths,
    DateTime SubmittedAt)
{
    public static ApplicationStatusDto? From(LoanApplication? a)
        => a is null
            ? null
            : new ApplicationStatusDto(
                a.Id.Value, a.PartnerId, a.PartnerName, a.Status.ToString(),
                a.RequestedAmount, a.Currency, a.TermMonths, a.SubmittedAt);
}
