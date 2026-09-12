using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LoanManagement.Shared.Messaging;

/// <summary>
/// Config-driven MassTransit transport selection shared by every service.
///
/// This is deliberately NOT a hand-rolled Strategy pattern. The transport is chosen
/// once at startup, and MassTransit already provides the transport abstraction — our
/// job is only to select and wire the right one from configuration. Consumers,
/// publishers, and the outbox never change when the transport changes.
///
/// Queue transports (RabbitMQ, Amazon SQS, Azure Service Bus) all get the same
/// standardized retry + dead-letter behaviour. Kafka is a different model (a log with
/// consumer groups/offsets and no native DLQ) and is intentionally left as a
/// documented, not-yet-wired option.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <param name="registerConsumers">
    /// Optional hook for a service to register its consumers on the bus
    /// (e.g. <c>cfg =&gt; cfg.AddConsumers(typeof(SomeConsumer).Assembly)</c>).
    /// </param>
    public static IServiceCollection AddLoanManagementMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? registerConsumers = null)
    {
        var options = MessagingOptions.FromConfiguration(configuration);

        services.AddMassTransit(bus =>
        {
            registerConsumers?.Invoke(bus);

            switch (options.Transport)
            {
                case MessagingTransport.RabbitMq:
                    bus.UsingRabbitMq((context, cfg) =>
                    {
                        cfg.Host(options.RabbitMq.Host, h =>
                        {
                            h.Username(options.RabbitMq.Username);
                            h.Password(options.RabbitMq.Password);
                        });
                        ApplyStandardRetry(cfg, options);
                        cfg.ConfigureEndpoints(context);
                    });
                    break;

                case MessagingTransport.AmazonSqs:
                    bus.UsingAmazonSqs((context, cfg) =>
                    {
                        cfg.Host(options.AmazonSqs.Region, h =>
                        {
                            if (!string.IsNullOrWhiteSpace(options.AmazonSqs.AccessKey))
                                h.AccessKey(options.AmazonSqs.AccessKey);
                            if (!string.IsNullOrWhiteSpace(options.AmazonSqs.SecretKey))
                                h.SecretKey(options.AmazonSqs.SecretKey);
                        });
                        // MassTransit maps published messages to SNS topics and each
                        // consumer to its own SQS queue, with an _error redrive queue.
                        ApplyStandardRetry(cfg, options);
                        cfg.ConfigureEndpoints(context);
                    });
                    break;

                case MessagingTransport.AzureServiceBus:
                    bus.UsingAzureServiceBus((context, cfg) =>
                    {
                        cfg.Host(options.AzureServiceBus.ConnectionString ?? string.Empty);
                        // Topics + subscriptions per consumer, with the Service Bus
                        // dead-letter sub-queue for messages that exhaust retries.
                        ApplyStandardRetry(cfg, options);
                        cfg.ConfigureEndpoints(context);
                    });
                    break;

                case MessagingTransport.Kafka:
                    // Kafka is a RIDER in MassTransit, not a bus transport: it attaches
                    // to an existing bus and models topics/consumer-groups/offsets, with
                    // NO native dead-letter queue (you emulate it with a dead-letter TOPIC).
                    // It therefore cannot slot into the UsingXxx shape above. Wiring it is
                    // a documented later deep-dive; fail fast rather than pretend it works.
                    throw new NotSupportedException(
                        "Messaging:Transport=Kafka is a documented but not-yet-wired option. " +
                        "Kafka is a MassTransit rider (log/offset model, no native DLQ) and needs " +
                        "the MassTransit.Kafka package plus a bus + rider setup. Use InMemory, " +
                        "RabbitMq, AmazonSqs, or AzureServiceBus, or wire the Kafka rider in a deep-dive.");

                case MessagingTransport.InMemory:
                default:
                    bus.UsingInMemory((context, cfg) =>
                    {
                        ApplyStandardRetry(cfg, options);
                        cfg.ConfigureEndpoints(context);
                    });
                    break;
            }
        });

        return services;
    }

    /// <summary>
    /// Standardized retry applied to every queue transport: N retries with exponential
    /// backoff, after which MassTransit moves the message to the transport's dead-letter
    /// (<c>_error</c>) queue. Steering default: 3 retries, 1s → 8s.
    /// </summary>
    private static void ApplyStandardRetry(IBusFactoryConfigurator cfg, MessagingOptions options)
    {
        cfg.UseMessageRetry(r => r.Exponential(
            retryLimit: options.RetryCount,
            minInterval: TimeSpan.FromSeconds(options.RetryMinIntervalSeconds),
            maxInterval: TimeSpan.FromSeconds(options.RetryMaxIntervalSeconds),
            intervalDelta: TimeSpan.FromSeconds(options.RetryMinIntervalSeconds)));
    }
}
