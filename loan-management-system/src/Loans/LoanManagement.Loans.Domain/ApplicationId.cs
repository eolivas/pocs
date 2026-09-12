namespace LoanManagement.Loans.Domain;

/// <summary>Reference to the originating loan application (owned by the Origination service).</summary>
public readonly record struct ApplicationId(Guid Value);
