using FluentAssertions;
using LoanManagement.Origination.Domain;
using LoanManagement.Origination.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LoanManagement.Origination.IntegrationTests;

/// <summary>
/// Proves the write-side availability property: submitting an application only writes the
/// local DB + outbox, so it succeeds and is durable even with NO downstream services running.
/// </summary>
public class AcceptAndQueueTests
{
    [Fact]
    public async Task Submit_PersistsApplication_AndQueuesEvent_WithNoDownstream()
    {
        using var sut = new SqliteOriginationDb();
        var repo = new LoanApplicationRepository(sut.Context);

        // No broker, no Credit Engine, no Contracts — nothing but the local DB + outbox.
        var application = LoanApplication.Submit(
            partnerId: Guid.NewGuid(), partnerName: "Acme Retail",
            requestedAmount: 3000m, currency: "USD", termMonths: 12);
        await repo.AddAsync(application);
        await repo.SaveChangesAsync();

        await using var read = sut.NewContext();
        (await read.Applications.CountAsync()).Should().Be(1, "the application is durable regardless of downstream");
        read.Applications.Single().Status.Should().Be(ApplicationStatus.Submitted);

        // The submission event is queued in the outbox for asynchronous downstream delivery.
        read.OutboxMessages.Count(m => m.Type.Contains("LoanApplicationSubmittedEvent"))
            .Should().Be(1, "submission accept-and-queues exactly one event");
    }

    [Fact]
    public void Submit_InvalidInput_ThrowsBeforePersisting()
    {
        var act = () => LoanApplication.Submit(Guid.NewGuid(), "Acme", requestedAmount: 0m, currency: "USD", termMonths: 12);
        act.Should().Throw<Domain.Exceptions.ApplicationDomainException>();
    }
}
