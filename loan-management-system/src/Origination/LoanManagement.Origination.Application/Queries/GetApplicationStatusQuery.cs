using LoanManagement.Origination.Application.DTOs;
using MediatR;

namespace LoanManagement.Origination.Application.Queries;

public sealed record GetApplicationStatusQuery(Guid ApplicationId) : IRequest<ApplicationStatusDto?>;
