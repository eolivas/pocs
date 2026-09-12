using LoanManagement.Contracts.Application;
using LoanManagement.Contracts.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Layer registrations (Clean Architecture): Application + Infrastructure.
builder.Services.AddContractsApplication();
builder.Services.AddContractsInfrastructure(builder.Configuration);

// Health checks. Readiness will add DB/broker dependency checks in a later deep-dive.
builder.Services.AddHealthChecks();

var app = builder.Build();

// Liveness: process is up. No dependency checks.
app.MapHealthChecks("/health/live");

// Readiness: safe to route traffic. Dependency checks added later.
app.MapHealthChecks("/health/ready");

app.MapGet("/", () => Results.Ok(new { service = "Contracts", status = "scaffold" }));

app.Run();
