namespace LoanManagement.Bff.Application.Composition;

/// <summary>
/// Registers one resilient typed <see cref="HttpClient"/> per origination-facing
/// downstream service (retry with backoff, circuit breaker, timeout), so a slow
/// or failing service does not cascade into the application experience.
/// Actual composition endpoints are implemented in a later deep-dive.
/// </summary>
public static class BffServiceCollectionExtensions
{
    public static IServiceCollection AddDownstreamServiceClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        foreach (var name in DownstreamServices.All)
        {
            services
                .AddHttpClient(name, client =>
                {
                    var baseAddress = configuration[$"Services:{name}"];
                    if (!string.IsNullOrWhiteSpace(baseAddress))
                    {
                        client.BaseAddress = new Uri(baseAddress);
                    }

                    client.Timeout = TimeSpan.FromSeconds(5);
                })
                .AddStandardResilienceHandler();
        }

        services.AddScoped<ApplicationStatusComposer>();

        return services;
    }
}
