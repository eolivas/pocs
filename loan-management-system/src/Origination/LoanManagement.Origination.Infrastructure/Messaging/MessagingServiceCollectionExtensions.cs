using LoanManagement.Shared.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.Origination.Infrastructure.Messaging;

/// <summary>
/// Delegates to the shared, config-driven transport selector
/// (<see cref="MessagingServiceCollectionExtensions.AddLoanManagementMessaging"/>).
/// The transport (InMemory / RabbitMq / AmazonSqs / AzureServiceBus / Kafka) is chosen
/// from the <c>Messaging</c> configuration section. Consumers for this service would be
/// registered via the optional callback in a later deep-dive.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
        => services.AddLoanManagementMessaging(configuration);
}
