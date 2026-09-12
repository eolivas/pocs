using LoanManagement.Collections.Infrastructure.Messaging;
using LoanManagement.Collections.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.Collections.Infrastructure;

/// <summary>
/// Registers Infrastructure-layer services for the Collections / Recovery service: EF Core
/// (SQL Server) DbContext and MassTransit messaging. Database-per-service: the
/// connection string key is <c>CollectionsDb</c>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCollectionsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("CollectionsDb");

        services.AddDbContext<CollectionsDbContext>(options =>
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
