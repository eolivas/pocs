using LoanManagement.Communications.Infrastructure.Messaging;
using LoanManagement.Communications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.Communications.Infrastructure;

/// <summary>
/// Registers Infrastructure-layer services for the Communications service: EF Core
/// (SQL Server) DbContext and MassTransit messaging. Database-per-service: the
/// connection string key is <c>CommunicationsDb</c>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCommunicationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("CommunicationsDb");

        services.AddDbContext<CommunicationsDbContext>(options =>
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
