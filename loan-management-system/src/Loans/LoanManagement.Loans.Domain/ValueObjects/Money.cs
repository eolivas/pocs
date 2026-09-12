namespace LoanManagement.Loans.Domain.ValueObjects;

/// <summary>
/// A monetary amount in a single currency. POC assumption: single currency, but the
/// currency is carried so arithmetic across mismatched currencies fails fast.
/// </summary>
public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative.", nameof(amount));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required.", nameof(currency));

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public static Money Zero(string currency) => new(0m, currency);

    public bool IsPositive => Amount > 0m;
}
