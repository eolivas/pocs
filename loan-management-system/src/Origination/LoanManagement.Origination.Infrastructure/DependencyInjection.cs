using LoanManagement.Origination.Domain;
using LoanManagement.Origination.Infrastructure.Messaging;
using LoanManagement.Origination.Infrastructure.Persistence;
using LoanManagement.Shared.Kernel;
using LoanManagement.Shared.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.Origination.Infrastructure;

/// <summary>
/// Registers Infrastructure-layer services for Origination: EF Core (SQL Server) DbContext,
/// the application repository + deduplication, the outbox processor (hosted), and MassTransit
/// with the status-projection consumers (CreditDecisionMade / ContractSigned) registered.
/// Submission only writes the local DB + outbox, so it never blocks on downstream services.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddOriginationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("OriginationDb");

        services.AddDbContext<OriginationDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseSqlServer(connectionString);
            }
        });

        services.AddScoped<ILoanApplicationRepository, LoanApplicationRepository>();
        services.AddScoped<IDeduplicationStore, DeduplicationStore>();

        services.AddHostedService<OutboxProcessor>();

        services.AddLoanManagementMessaging(
            configuration,
            bus =>
            {
                bus.AddConsumer<CreditDecisionMadeConsumer>();
                bus.AddConsumer<ContractSignedConsumer>();
            });

        return services;
    }
}
