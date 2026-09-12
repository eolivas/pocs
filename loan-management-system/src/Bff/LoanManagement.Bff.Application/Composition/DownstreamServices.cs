namespace LoanManagement.Bff.Application.Composition;

/// <summary>
/// Logical names for the services the Application BFF composes from. These are the
/// pre-funding, origination-facing services. Base addresses come from configuration
/// (<c>Services:{Name}</c>).
/// </summary>
public static class DownstreamServices
{
    public const string Origination = "Origination";
    public const string CreditEngine = "CreditEngine";
    public const string Contracts = "Contracts";
    public const string Communications = "Communications";

    public static readonly string[] All = [Origination, CreditEngine, Contracts, Communications];
}
