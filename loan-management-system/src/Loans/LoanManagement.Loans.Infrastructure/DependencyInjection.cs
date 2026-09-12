using LoanManagement.Loans.Application.Interfaces;
using LoanManagement.Loans.Domain;
using LoanManagement.Loans.Infrastructure.Funding;
using LoanManagement.Loans.Infrastructure.Messaging;
using LoanManagement.Loans.Infrastructure.Persistence;
using LoanManagement.Loans.Infrastructure.Sagas;
using LoanManagement.Shared.Kernel;
using LoanManagement.Shared.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.Loans.Infrastructure;

/// <summary>
/// Registers Infrastructure-layer services for the Loans (LSS) service:
/// EF Core (SQL Server) DbContext, the repository + deduplication store + funding
/// service, the outbox processor (hosted), and MassTransit messaging with the funding
/// saga registered on the bus. Database-per-service: connection string key is <c>LoansDb</c>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddLoansInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LoansDb");

        services.AddDbContext<LoansDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseSqlServer(connectionString);
            }
        });

        // Persistence + funding services.
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<IDeduplicationStore, DeduplicationStore>();
        services.AddScoped<IFundingService, FundingService>();

        // Outbox processor: publishes captured domain events reliably.
        services.AddHostedService<OutboxProcessor>();

        // MassTransit with the transport chosen by config; register the funding saga on
        // the bus via the shared selector's consumer-registration hook.
        services.AddLoanManagementMessaging(
            configuration,
            bus => bus.AddSagaStateMachine<FundingStateMachine, FundingSagaState>().InMemoryRepository());

        return services;
    }
}
