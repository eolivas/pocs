namespace LoanManagement.Origination.Domain;

/// <summary>
/// Lifecycle of a loan application in Origination. Once <c>Submitted</c>, the application
/// is durable and its downstream progress (decision, contract) advances asynchronously.
/// </summary>
public enum ApplicationStatus
{
    Submitted = 0,
    Decisioned = 1,
    Contracted = 2,
    Rejected = 3,
    Withdrawn = 4,
}
