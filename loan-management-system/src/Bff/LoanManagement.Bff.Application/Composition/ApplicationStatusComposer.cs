using System.Net.Http.Json;

namespace LoanManagement.Bff.Application.Composition;

/// <summary>
/// Composes the applicant's status view with graceful degradation. Origination's local
/// status (accept-and-queue + local read) is the PRIMARY source and is treated as always
/// available. Credit Engine detail is OPTIONAL enrichment — if it's unavailable, the
/// response is partial with <c>Degraded = true</c> rather than a 500, so the applicant
/// still sees that their application was received and its last known status.
/// </summary>
public sealed class ApplicationStatusComposer(IHttpClientFactory httpClientFactory)
{
    public async Task<ApplicationStatusResponse> ComposeAsync(Guid applicationId, CancellationToken ct = default)
    {
        var origination = httpClientFactory.CreateClient(DownstreamServices.Origination);
        var status = await origination.GetFromJsonAsync<ApplicationStatusSummary>(
            $"/api/applications/{applicationId}/status", ct);

        if (status is null)
            return new ApplicationStatusResponse(applicationId, null, null, Degraded: false, []);

        var degradedReasons = new List<string>();
        object? creditDetail = null;
        try
        {
            var credit = httpClientFactory.CreateClient(DownstreamServices.CreditEngine);
            creditDetail = await credit.GetFromJsonAsync<object>(
                $"/api/credit/by-application/{applicationId}", ct);
        }
        catch (Exception ex)
        {
            degradedReasons.Add($"credit enrichment unavailable: {ex.GetType().Name}");
        }

        return new ApplicationStatusResponse(
            applicationId, status, creditDetail, degradedReasons.Count > 0, degradedReasons);
    }
}

public sealed record ApplicationStatusSummary(
    Guid ApplicationId,
    string PartnerName,
    string Status,
    decimal RequestedAmount,
    string Currency,
    int TermMonths,
    DateTime SubmittedAt);

public sealed record ApplicationStatusResponse(
    Guid ApplicationId,
    ApplicationStatusSummary? Status,
    object? CreditDetail,
    bool Degraded,
    IReadOnlyList<string> DegradedReasons);
