using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.CreditEngine.Application;

/// <summary>
/// Registers Application-layer services for the Credit Engine service. Wired but empty
/// for the POC scaffold â€” handlers and validators are added in a later deep-dive.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCreditEngineApplication(this IServiceCollection services)
    {
        // services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        // services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        return services;
    }
}
