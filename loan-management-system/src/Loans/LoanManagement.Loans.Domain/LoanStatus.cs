namespace LoanManagement.Loans.Domain;

/// <summary>
/// The funding lifecycle of a loan inside the Loans service.
/// <c>PendingFunding → Funded → Boarded</c> is the happy path; <c>FundingFailed</c>
/// is the terminal compensation state.
/// </summary>
public enum LoanStatus
{
    PendingFunding = 0,
    Funded = 1,
    Boarded = 2,
    FundingFailed = 3,
}
