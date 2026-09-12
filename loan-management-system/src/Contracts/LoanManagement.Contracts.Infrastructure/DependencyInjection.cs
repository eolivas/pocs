using LoanManagement.Contracts.Infrastructure.Messaging;
using LoanManagement.Contracts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.Contracts.Infrastructure;

/// <summary>
/// Registers Infrastructure-layer services for the Contracts service: EF Core
/// (SQL Server) DbContext and MassTransit messaging. Database-per-service: the
/// connection string key is <c>ContractsDb</c>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddContractsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ContractsDb");

        services.AddDbContext<ContractsDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseSqlServer(connectionString);
            }
        });

        services.AddMessaging(configuration);

        return services;
    }
}
