using LoanManagement.CustomerPortal.Application;
using LoanManagement.CustomerPortal.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Layer registrations (Clean Architecture): Application + Infrastructure.
builder.Services.AddCustomerPortalApplication();
builder.Services.AddCustomerPortalInfrastructure(builder.Configuration);

// Health checks. Readiness will add DB/broker dependency checks in a later deep-dive.
builder.Services.AddHealthChecks();

var app = builder.Build();

// Liveness: process is up. No dependency checks.
app.MapHealthChecks("/health/live");

// Readiness: safe to route traffic. Dependency checks added later.
app.MapHealthChecks("/health/ready");

app.MapGet("/", () => Results.Ok(new { service = "Customer Portal", status = "ready" }));

// Borrower loans, served from the LOCAL projection only. This read path has no runtime
// dependency on Loans/Contracts, so it stays available during downstream outages.
app.MapGet("/api/portal/applications/{applicationId:guid}/loans",
    async (Guid applicationId, MediatR.IMediator mediator) =>
    {
        var loans = await mediator.Send(new LoanManagement.CustomerPortal.Application.Queries.GetBorrowerLoansQuery(applicationId));
        return Results.Ok(loans);
    });

app.Run();
