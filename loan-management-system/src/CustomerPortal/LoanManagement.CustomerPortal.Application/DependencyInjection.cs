using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.CustomerPortal.Application;

/// <summary>
/// Registers Application-layer services for the Customer Portal service: MediatR handlers
/// (the borrower-loan queries that read the local projection).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCustomerPortalApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        return services;
    }
}
