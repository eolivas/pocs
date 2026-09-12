using LoanManagement.Bff.Application.Composition;

var builder = WebApplication.CreateBuilder(args);

// Resilient typed HTTP clients to the pre-funding, origination-facing services.
builder.Services.AddDownstreamServiceClients(builder.Configuration);

// CORS for the Application (Originations) Frontend (dev origin configurable).
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var appOrigin = builder.Configuration["ApplicationFrontend:Origin"] ?? "http://localhost:5174";
        policy.WithOrigins(appOrigin).AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseCors();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

// Applicant status with graceful degradation. Origination's local status is primary
// (always available); Credit Engine detail is optional enrichment. If enrichment is down,
// the response is partial with degraded=true rather than a 500.
app.MapGet("/api/application/{applicationId:guid}/status",
    async (Guid applicationId, ApplicationStatusComposer composer, CancellationToken ct) =>
    {
        var result = await composer.ComposeAsync(applicationId, ct);
        return result.Status is null ? Results.NotFound() : Results.Ok(result);
    });

app.Run();
