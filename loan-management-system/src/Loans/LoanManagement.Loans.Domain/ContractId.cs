namespace LoanManagement.Loans.Domain;

/// <summary>Reference to the signed contract (owned by the Contracts service) that this loan funds.</summary>
public readonly record struct ContractId(Guid Value);
