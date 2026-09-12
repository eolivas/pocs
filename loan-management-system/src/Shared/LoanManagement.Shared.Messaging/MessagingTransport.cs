namespace LoanManagement.Shared.Messaging;

/// <summary>
/// The message transport a service uses, selected once at startup via configuration
/// (<c>Messaging:Transport</c>). Consumers, publishers, and the outbox are identical
/// across all of these — only the bus wiring differs.
/// </summary>
public enum MessagingTransport
{
    /// <summary>In-process transport. Local dev / tests only — nothing leaves the process.</summary>
    InMemory = 0,

    /// <summary>RabbitMQ broker. Per-consumer queues + <c>_error</c> dead-letter queue.</summary>
    RabbitMq = 1,

    /// <summary>Amazon SNS (topics) + SQS (per-consumer queues) with redrive to an <c>_error</c> queue.</summary>
    AmazonSqs = 2,

    /// <summary>Azure Service Bus topics + subscriptions/queues with dead-letter support.</summary>
    AzureServiceBus = 3,

    /// <summary>
    /// Apache Kafka. A distributed <b>log</b> (consumer groups, partitions, offsets) rather than a
    /// queue — no native dead-letter queue. Selectable but NOT wired in this POC; see
    /// <see cref="MessagingServiceCollectionExtensions"/> for the documented rider approach.
    /// </summary>
    Kafka = 4,
}
