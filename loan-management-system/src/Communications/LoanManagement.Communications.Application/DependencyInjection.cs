using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.Communications.Application;

/// <summary>
/// Registers Application-layer services for the Communications service. Wired but empty
/// for the POC scaffold â€” handlers and validators are added in a later deep-dive.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCommunicationsApplication(this IServiceCollection services)
    {
        // services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        // services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        return services;
    }
}
