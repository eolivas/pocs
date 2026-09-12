using Microsoft.Extensions.Configuration;

namespace LoanManagement.Shared.Messaging;

/// <summary>
/// Bound from the <c>Messaging</c> configuration section. Drives transport selection
/// and the standardized retry / dead-letter behaviour applied to the queue-based
/// transports (RabbitMQ, Amazon SQS, Azure Service Bus).
/// </summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    /// <summary>
    /// Reads the <c>Messaging</c> section via the <see cref="IConfiguration"/> indexer
    /// (no Configuration.Binder dependency, to avoid transitive version conflicts under CPM).
    /// Missing keys fall back to the defaults on this type.
    /// </summary>
    public static MessagingOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var o = new MessagingOptions();

        if (Enum.TryParse<MessagingTransport>(section["Transport"], ignoreCase: true, out var transport))
            o.Transport = transport;

        if (int.TryParse(section["RetryCount"], out var rc)) o.RetryCount = rc;
        if (int.TryParse(section["RetryMinIntervalSeconds"], out var rmin)) o.RetryMinIntervalSeconds = rmin;
        if (int.TryParse(section["RetryMaxIntervalSeconds"], out var rmax)) o.RetryMaxIntervalSeconds = rmax;

        o.RabbitMq.Host = section["RabbitMq:Host"] ?? o.RabbitMq.Host;
        o.RabbitMq.Username = section["RabbitMq:Username"] ?? o.RabbitMq.Username;
        o.RabbitMq.Password = section["RabbitMq:Password"] ?? o.RabbitMq.Password;

        o.AmazonSqs.Region = section["AmazonSqs:Region"] ?? o.AmazonSqs.Region;
        o.AmazonSqs.AccessKey = section["AmazonSqs:AccessKey"];
        o.AmazonSqs.SecretKey = section["AmazonSqs:SecretKey"];

        o.AzureServiceBus.ConnectionString = section["AzureServiceBus:ConnectionString"];

        return o;
    }

    /// <summary>Which transport to wire up. Defaults to <see cref="MessagingTransport.InMemory"/>.</summary>
    public MessagingTransport Transport { get; set; } = MessagingTransport.InMemory;

    /// <summary>
    /// Message retry before dead-lettering. Steering default: 3 retries, exponential
    /// 1s → 8s, then the message goes to the transport's <c>_error</c> dead-letter queue.
    /// </summary>
    public int RetryCount { get; set; } = 3;
    public int RetryMinIntervalSeconds { get; set; } = 1;
    public int RetryMaxIntervalSeconds { get; set; } = 8;

    /// <summary>RabbitMQ settings (used when <see cref="Transport"/> is RabbitMq).</summary>
    public RabbitMqOptions RabbitMq { get; set; } = new();

    /// <summary>Amazon SQS/SNS settings (used when <see cref="Transport"/> is AmazonSqs).</summary>
    public AmazonSqsOptions AmazonSqs { get; set; } = new();

    /// <summary>Azure Service Bus settings (used when <see cref="Transport"/> is AzureServiceBus).</summary>
    public AzureServiceBusOptions AzureServiceBus { get; set; } = new();

    public sealed class RabbitMqOptions
    {
        public string Host { get; set; } = "rabbitmq";
        public string Username { get; set; } = "guest";
        public string Password { get; set; } = "guest";
    }

    public sealed class AmazonSqsOptions
    {
        public string Region { get; set; } = "us-east-1";
        // Access keys are injected from Secrets Manager in real environments; blank here.
        public string? AccessKey { get; set; }
        public string? SecretKey { get; set; }
    }

    public sealed class AzureServiceBusOptions
    {
        // Connection string is injected from Key Vault in real environments; blank here.
        public string? ConnectionString { get; set; }
    }
}
