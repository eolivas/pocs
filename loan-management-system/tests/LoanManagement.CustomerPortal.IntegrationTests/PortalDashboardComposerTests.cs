using System.Net;
using System.Text;
using FluentAssertions;
using LoanManagement.Bff.Portal.Composition;

namespace LoanManagement.CustomerPortal.IntegrationTests;

/// <summary>
/// Proves the Portal BFF degrades gracefully: when an enrichment downstream is unavailable,
/// the dashboard is still returned (partial, Degraded=true) rather than failing — the AP
/// promise for the borrower portal.
/// </summary>
public class PortalDashboardComposerTests
{
    // Returns a canned response for one client name and throws for another.
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(responder(request));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly Dictionary<string, Func<HttpClient>> _clients = new();
        public void Register(string name, Func<HttpClient> factory) => _clients[name] = factory;
        public HttpClient CreateClient(string name) => _clients[name]();
    }

    private static HttpClient JsonClient(string json) =>
        new(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }))
        { BaseAddress = new Uri("http://localhost") };

    private static HttpClient ThrowingClient() =>
        new(new StubHandler(_ => throw new HttpRequestException("downstream unavailable")))
        { BaseAddress = new Uri("http://localhost") };

    [Fact]
    public async Task Enrichment_Down_ReturnsPartial_WithDegradedTrue_AndPrimaryData()
    {
        var factory = new StubHttpClientFactory();
        // PRIMARY (Customer Portal local projection): available, returns one loan.
        factory.Register(DownstreamServices.CustomerPortal,
            () => JsonClient("""[{"loanId":"11111111-1111-1111-1111-111111111111","status":"Boarded","amount":5000,"currency":"USD","fundedAt":null,"boardedAt":null}]"""));
        // ENRICHMENT (Contracts): down.
        factory.Register(DownstreamServices.Contracts, ThrowingClient);

        var composer = new PortalDashboardComposer(factory);
        var result = await composer.ComposeAsync(Guid.NewGuid());

        result.Degraded.Should().BeTrue();
        result.DegradedReasons.Should().NotBeEmpty();
        result.Loans.Should().HaveCount(1, "the borrower still sees their loans from the local projection");
    }

    [Fact]
    public async Task AllHealthy_ReturnsComplete_NotDegraded()
    {
        var factory = new StubHttpClientFactory();
        factory.Register(DownstreamServices.CustomerPortal,
            () => JsonClient("""[{"loanId":"11111111-1111-1111-1111-111111111111","status":"Funded","amount":5000,"currency":"USD","fundedAt":null,"boardedAt":null}]"""));
        factory.Register(DownstreamServices.Contracts, () => JsonClient("""{"ok":true}"""));

        var composer = new PortalDashboardComposer(factory);
        var result = await composer.ComposeAsync(Guid.NewGuid());

        result.Degraded.Should().BeFalse();
        result.Loans.Should().HaveCount(1);
    }
}
