using LoanManagement.CreditEngine.Infrastructure.Messaging;
using LoanManagement.CreditEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.CreditEngine.Infrastructure;

/// <summary>
/// Registers Infrastructure-layer services for the Credit Engine service: EF Core
/// (SQL Server) DbContext and MassTransit messaging. Database-per-service: the
/// connection string key is <c>CreditDb</c>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCreditEngineInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("CreditDb");

        services.AddDbContext<CreditEngineDbContext>(options =>
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
