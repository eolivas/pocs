using LoanManagement.CustomerPortal.Application.Interfaces;
using LoanManagement.CustomerPortal.Infrastructure.Messaging;
using LoanManagement.CustomerPortal.Infrastructure.Persistence;
using LoanManagement.CustomerPortal.Infrastructure.Projections;
using LoanManagement.Shared.Kernel;
using LoanManagement.Shared.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.CustomerPortal.Infrastructure;

/// <summary>
/// Registers Infrastructure-layer services for the Customer Portal: EF Core (SQL Server)
/// DbContext, the local-projection read store, the projection builder + deduplication,
/// and MassTransit with the projection consumers (LoanFunded / LoanBoarded) registered.
/// The read store touches only <c>PortalDb</c>, so borrower reads survive downstream outages.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCustomerPortalInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PortalDb");

        services.AddDbContext<CustomerPortalDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseSqlServer(connectionString);
            }
        });

        services.AddScoped<IDeduplicationStore, DeduplicationStore>();
        services.AddScoped<IBorrowerLoanReadStore, BorrowerLoanReadStore>();
        services.AddScoped<BorrowerLoanProjection>();

        // MassTransit with the config-selected transport; register the projection consumers
        // via the shared selector's consumer-registration hook.
        services.AddLoanManagementMessaging(
            configuration,
            bus =>
            {
                bus.AddConsumer<LoanFundedConsumer>();
                bus.AddConsumer<LoanBoardedConsumer>();
            });

        return services;
    }
}
