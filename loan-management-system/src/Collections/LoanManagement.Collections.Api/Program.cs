using LoanManagement.Collections.Application;
using LoanManagement.Collections.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Layer registrations (Clean Architecture): Application + Infrastructure.
builder.Services.AddCollectionsApplication();
builder.Services.AddCollectionsInfrastructure(builder.Configuration);

// Health checks. Readiness will add DB/broker dependency checks in a later deep-dive.
builder.Services.AddHealthChecks();

var app = builder.Build();

// Liveness: process is up. No dependency checks.
app.MapHealthChecks("/health/live");

// Readiness: safe to route traffic. Dependency checks added later.
app.MapHealthChecks("/health/ready");

app.MapGet("/", () => Results.Ok(new { service = "Collections / Recovery", status = "scaffold" }));

app.Run();
