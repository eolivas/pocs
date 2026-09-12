namespace LoanManagement.Loans.Domain;

public readonly record struct LoanId(Guid Value)
{
    public static LoanId New() => new(Guid.NewGuid());
}
