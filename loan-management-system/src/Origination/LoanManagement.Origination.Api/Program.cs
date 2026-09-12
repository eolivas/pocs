using LoanManagement.Origination.Application;
using LoanManagement.Origination.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Layer registrations (Clean Architecture): Application + Infrastructure.
builder.Services.AddOriginationApplication();
builder.Services.AddOriginationInfrastructure(builder.Configuration);

// Health checks. Readiness will add DB/broker dependency checks in a later deep-dive.
builder.Services.AddHealthChecks();

var app = builder.Build();

// Liveness: process is up. No dependency checks — used by orchestrators for restart decisions.
app.MapHealthChecks("/health/live");

// Readiness: safe to route traffic. Dependency checks (DB, broker) added later.
app.MapHealthChecks("/health/ready");

app.MapGet("/", () => Results.Ok(new { service = "Origination (LOS)", status = "ready" }));

// Accept-and-queue: submission only writes the local DB + outbox and returns the new
// application id. It succeeds even if Credit Engine / Contracts are down — downstream
// decisioning happens asynchronously off the LoanApplicationSubmitted event.
app.MapPost("/api/applications",
    async (LoanManagement.Origination.Application.Commands.SubmitApplicationCommand command, MediatR.IMediator mediator) =>
    {
        var applicationId = await mediator.Send(command);
        return Results.Accepted($"/api/applications/{applicationId}/status", new { applicationId, status = "Submitted" });
    });

// Status read, served from the LOCAL OriginationDb — available during downstream outages.
app.MapGet("/api/applications/{applicationId:guid}/status",
    async (Guid applicationId, MediatR.IMediator mediator) =>
    {
        var status = await mediator.Send(new LoanManagement.Origination.Application.Queries.GetApplicationStatusQuery(applicationId));
        return status is null ? Results.NotFound() : Results.Ok(status);
    });

app.Run();
