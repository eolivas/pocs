using LoanManagement.Origination.Application.DTOs;
using LoanManagement.Origination.Domain;
using MediatR;
using ApplicationId = LoanManagement.Origination.Domain.ApplicationId;

namespace LoanManagement.Origination.Application.Queries;

/// <summary>Reads the application's status from the LOCAL OriginationDb — no dependency on Credit/Contracts at read time.</summary>
public sealed class GetApplicationStatusHandler(ILoanApplicationRepository repository)
    : IRequestHandler<GetApplicationStatusQuery, ApplicationStatusDto?>
{
    public async Task<ApplicationStatusDto?> Handle(GetApplicationStatusQuery request, CancellationToken cancellationToken)
    {
        var application = await repository.GetByIdAsync(new ApplicationId(request.ApplicationId), cancellationToken);
        return ApplicationStatusDto.From(application);
    }
}
