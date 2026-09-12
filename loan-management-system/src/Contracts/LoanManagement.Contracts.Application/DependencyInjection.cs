using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.Contracts.Application;

/// <summary>
/// Registers Application-layer services for the Contracts service. Wired but empty
/// for the POC scaffold â€” handlers and validators are added in a later deep-dive.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddContractsApplication(this IServiceCollection services)
    {
        // services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        // services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        return services;
    }
}
