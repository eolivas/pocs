using System.Text.Json;
using LoanManagement.Origination.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LoanManagement.Origination.Infrastructure.Messaging;

/// <summary>
/// Publishes Origination outbox messages to the broker reliably (poll → publish → mark
/// processed; retry then dead-letter). Decouples submission from downstream availability:
/// the submit path only writes the DB + outbox, and this processor delivers the event
/// whenever the broker/consumers are reachable.
/// </summary>
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IPublishEndpoint publishEndpoint,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private const int MaxRetries = 5;
    private const int BatchSize = 20;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Origination outbox processing loop failed; retrying next interval.");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OriginationDbContext>();

        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.FailedAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in pending)
        {
            try
            {
                var eventType = System.Type.GetType(message.Type)
                    ?? throw new InvalidOperationException($"Unknown event type '{message.Type}'.");

                var @event = JsonSerializer.Deserialize(message.Payload, eventType, SerializerOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize event '{message.Id}'.");

                await publishEndpoint.Publish(@event, eventType, cancellationToken);
                message.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                logger.LogWarning(ex, "Failed to publish outbox message {MessageId} (attempt {RetryCount}).",
                    message.Id, message.RetryCount);

                if (message.RetryCount >= MaxRetries)
                {
                    message.FailedAt = DateTime.UtcNow;
                    message.FailureReason = ex.Message;
                    logger.LogError(ex, "Outbox message {MessageId} dead-lettered after {MaxRetries} attempts.",
                        message.Id, MaxRetries);
                }
            }
        }

        if (pending.Count > 0)
            await db.SaveChangesAsync(cancellationToken);
    }
}
