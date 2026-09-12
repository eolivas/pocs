using LoanManagement.ComplianceReporting.Infrastructure.Messaging;
using LoanManagement.ComplianceReporting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.ComplianceReporting.Infrastructure;

/// <summary>
/// Registers Infrastructure-layer services for the Compliance & Reporting service: EF Core
/// (SQL Server) DbContext and MassTransit messaging. Database-per-service: the
/// connection string key is <c>ComplianceReportingDb</c>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddComplianceReportingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ComplianceReportingDb");

        services.AddDbContext<ComplianceReportingDbContext>(options =>
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
