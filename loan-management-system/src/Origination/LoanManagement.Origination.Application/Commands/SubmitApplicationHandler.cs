using LoanManagement.Origination.Domain;
using MediatR;

namespace LoanManagement.Origination.Application.Commands;

/// <summary>
/// Accept-and-queue: creates the application and persists it (which drains the
/// <c>LoanApplicationSubmitted</c> event to the outbox in the same transaction). Returns the
/// new application id. This succeeds even if Credit Engine / Contracts are down, because it
/// only writes to the local DB + outbox — downstream work is triggered asynchronously.
/// </summary>
public sealed class SubmitApplicationHandler(ILoanApplicationRepository repository)
    : IRequestHandler<SubmitApplicationCommand, Guid>
{
    public async Task<Guid> Handle(SubmitApplicationCommand request, CancellationToken cancellationToken)
    {
        var application = LoanApplication.Submit(
            request.PartnerId, request.PartnerName, request.RequestedAmount, request.Currency, request.TermMonths);

        await repository.AddAsync(application, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return application.Id.Value;
    }
}
