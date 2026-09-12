namespace LoanManagement.Origination.Domain;

public readonly record struct ApplicationId(Guid Value)
{
    public static ApplicationId New() => new(Guid.NewGuid());
}
