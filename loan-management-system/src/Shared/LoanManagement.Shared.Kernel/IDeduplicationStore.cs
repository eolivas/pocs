namespace LoanManagement.Shared.Kernel;

/// <summary>
/// Records which events a consumer has already processed, so that at-least-once
/// delivery is made effectively exactly-once. Consumers check <see cref="HasBeenProcessedAsync"/>
/// before doing side effects and call <see cref="MarkProcessedAsync"/> after.
/// </summary>
public interface IDeduplicationStore
{
    Task<bool> HasBeenProcessedAsync(Guid eventId, string consumer, CancellationToken cancellationToken = default);

    Task MarkProcessedAsync(Guid eventId, string consumer, CancellationToken cancellationToken = default);
}
