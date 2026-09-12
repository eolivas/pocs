namespace LoanManagement.Bff.Portal.Composition;

/// <summary>
/// Logical names for the services the Portal BFF composes from. These are the
/// post-funding, borrower-facing services. Base addresses come from configuration
/// (<c>Services:{Name}</c>).
/// </summary>
public static class DownstreamServices
{
    public const string Loans = "Loans";
    public const string Contracts = "Contracts";
    public const string CustomerPortal = "CustomerPortal";

    public static readonly string[] All = [Loans, Contracts, CustomerPortal];
}
