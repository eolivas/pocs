namespace LoanManagement.Loans.Domain.Exceptions;

/// <summary>Raised when a loan business rule / invariant is violated (not for infrastructure errors).</summary>
public sealed class LoanDomainException(string message) : Exception(message);
