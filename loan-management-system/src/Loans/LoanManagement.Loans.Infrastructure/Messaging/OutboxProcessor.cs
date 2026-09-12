using System.Text.Json;
using LoanManagement.Loans.Infrastructure.Persistence;
using LoanManagement.Shared.Kernel;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LoanManagement.Loans.Infrastructure.Messaging;

/// <summary>
/// Publishes outbox messages to the broker reliably. Polls for pending rows
/// (<c>ProcessedAt IS NULL AND FailedAt IS NULL</c>), deserializes each event, publishes
/// it via MassTransit (propagating the correlation id), and marks it processed. On
/// failure it increments <c>RetryCount</c>; after <see cref="MaxRetries"/> it dead-letters
/// the row (<c>FailedAt</c> + reason) so one poison message can't block the rest.
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
                logger.LogError(ex, "Outbox processing loop failed; will retry next interval.");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LoansDbContext>();

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

                await publishEndpoint.Publish(@event, eventType, ctx =>
                {
                    if (!string.IsNullOrWhiteSpace(message.CorrelationId))
                        ctx.Headers.Set("X-Correlation-Id", message.CorrelationId);
                }, cancellationToken);

                message.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                logger.LogWarning(ex,
                    "Failed to publish outbox message {MessageId} (attempt {RetryCount}).",
                    message.Id, message.RetryCount);

                if (message.RetryCount >= MaxRetries)
                {
                    message.FailedAt = DateTime.UtcNow;
                    message.FailureReason = ex.Message;
                    logger.LogError(ex,
                        "Outbox message {MessageId} dead-lettered after {MaxRetries} attempts.",
                        message.Id, MaxRetries);
                }
            }
        }

        if (pending.Count > 0)
            await db.SaveChangesAsync(cancellationToken);
    }
}
