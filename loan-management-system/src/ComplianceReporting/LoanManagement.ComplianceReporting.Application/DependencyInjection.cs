using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.ComplianceReporting.Application;

/// <summary>
/// Registers Application-layer services for the Compliance & Reporting service. Wired but empty
/// for the POC scaffold â€” handlers and validators are added in a later deep-dive.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddComplianceReportingApplication(this IServiceCollection services)
    {
        // services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        // services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        return services;
    }
}
