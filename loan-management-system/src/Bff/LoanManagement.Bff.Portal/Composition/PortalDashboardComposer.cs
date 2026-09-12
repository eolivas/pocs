using System.Net.Http.Json;

namespace LoanManagement.Bff.Portal.Composition;

/// <summary>
/// Composes the borrower dashboard for the Portal Frontend with **graceful degradation**.
///
/// The Customer Portal's local projection is the PRIMARY source and is treated as always
/// available (it reads its own DB, no cross-service dependency). Loans/Contracts are
/// OPTIONAL enrichment. If an enrichment call fails, times out, or its circuit is open,
/// the composer catches it and returns a partial response with <c>Degraded = true</c> and
/// the reasons — the borrower still sees their loans rather than an error page.
/// </summary>
public sealed class PortalDashboardComposer(IHttpClientFactory httpClientFactory)
{
    public async Task<PortalDashboardResponse> ComposeAsync(Guid applicationId, CancellationToken ct = default)
    {
        // PRIMARY: local projection. If this fails the dashboard genuinely can't render,
        // so we let it throw (the portal projection is the availability guarantee).
        var portal = httpClientFactory.CreateClient(DownstreamServices.CustomerPortal);
        var loans = await portal.GetFromJsonAsync<List<BorrowerLoanSummary>>(
            $"/api/portal/applications/{applicationId}/loans", ct) ?? [];

        var degradedReasons = new List<string>();

        // OPTIONAL enrichment from Contracts. Failure degrades, never fails the request.
        object? contractDetails = null;
        try
        {
            var contracts = httpClientFactory.CreateClient(DownstreamServices.Contracts);
            contractDetails = await contracts.GetFromJsonAsync<object>(
                $"/api/contracts/by-application/{applicationId}", ct);
        }
        catch (Exception ex)
        {
            degradedReasons.Add($"contracts enrichment unavailable: {ex.GetType().Name}");
        }

        return new PortalDashboardResponse(
            ApplicationId: applicationId,
            Loans: loans,
            ContractDetails: contractDetails,
            Degraded: degradedReasons.Count > 0,
            DegradedReasons: degradedReasons);
    }
}

/// <summary>Borrower loan summary as returned by the Customer Portal projection.</summary>
public sealed record BorrowerLoanSummary(
    Guid LoanId,
    string Status,
    decimal Amount,
    string Currency,
    DateTime? FundedAt,
    DateTime? BoardedAt);

/// <summary>Composed dashboard. <see cref="Degraded"/> signals partial data due to a downstream outage.</summary>
public sealed record PortalDashboardResponse(
    Guid ApplicationId,
    IReadOnlyList<BorrowerLoanSummary> Loans,
    object? ContractDetails,
    bool Degraded,
    IReadOnlyList<string> DegradedReasons);
