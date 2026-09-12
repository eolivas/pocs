using LoanManagement.Bff.Portal.Composition;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

// Resilient typed HTTP clients to the post-funding, portal-facing services.
builder.Services.AddDownstreamServiceClients(builder.Configuration);

// CORS for the Portal Frontend (dev origin configurable).
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var portalOrigin = builder.Configuration["Portal:Origin"] ?? "http://localhost:5173";
        policy.WithOrigins(portalOrigin).AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseCors();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

// Borrower dashboard with graceful degradation. Reads the Customer Portal's local
// projection (primary, always available) and enriches from Contracts; if enrichment is
// down the response is partial with degraded=true rather than a 500. Cached briefly.
app.MapGet("/api/portal/dashboard/{applicationId:guid}",
    async (Guid applicationId, PortalDashboardComposer composer, IMemoryCache cache, CancellationToken ct) =>
    {
        var cacheKey = $"dashboard:{applicationId}";
        if (cache.TryGetValue(cacheKey, out PortalDashboardResponse? cached) && cached is not null)
            return Results.Ok(cached);

        var dashboard = await composer.ComposeAsync(applicationId, ct);

        // Only cache complete responses; don't pin a degraded (partial) result.
        if (!dashboard.Degraded)
            cache.Set(cacheKey, dashboard, TimeSpan.FromSeconds(10));

        return Results.Ok(dashboard);
    });

app.Run();
