using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.Loans.Application;

/// <summary>
/// Registers Application-layer services for the Loans (LSS) service: MediatR handlers
/// (commands/queries) and FluentValidation validators from this assembly.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddLoansApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        return services;
    }
}
