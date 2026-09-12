using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.Origination.Application;

/// <summary>
/// Registers Application-layer services for Origination: MediatR handlers
/// (submit command + status query) and FluentValidation validators.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddOriginationApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        return services;
    }
}
