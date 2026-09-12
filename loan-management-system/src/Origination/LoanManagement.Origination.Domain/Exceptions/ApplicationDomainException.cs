namespace LoanManagement.Origination.Domain.Exceptions;

/// <summary>Raised when a loan-application business rule / invariant is violated.</summary>
public sealed class ApplicationDomainException(string message) : Exception(message);
