namespace LoanManagement.Shared.Kernel;

/// <summary>
/// Base class for entities, identified by a strongly-typed ID. Equality is by ID.
/// </summary>
public abstract class Entity<TId>
    where TId : struct
{
    public TId Id { get; protected init; }

    public override bool Equals(object? obj)
        => obj is Entity<TId> other && GetType() == other.GetType() && Id.Equals(other.Id);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
