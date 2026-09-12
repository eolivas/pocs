using LoanManagement.ComplianceReporting.Application;
using LoanManagement.ComplianceReporting.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Layer registrations (Clean Architecture): Application + Infrastructure.
builder.Services.AddComplianceReportingApplication();
builder.Services.AddComplianceReportingInfrastructure(builder.Configuration);

// Health checks. Readiness will add DB/broker dependency checks in a later deep-dive.
builder.Services.AddHealthChecks();

var app = builder.Build();

// Liveness: process is up. No dependency checks.
app.MapHealthChecks("/health/live");

// Readiness: safe to route traffic. Dependency checks added later.
app.MapHealthChecks("/health/ready");

app.MapGet("/", () => Results.Ok(new { service = "Compliance & Reporting", status = "scaffold" }));

app.Run();
