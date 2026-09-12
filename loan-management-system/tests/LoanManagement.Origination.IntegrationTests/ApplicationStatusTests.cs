using FluentAssertions;
using LoanManagement.Origination.Domain;
using LoanManagement.Origination.Domain.Exceptions;

namespace LoanManagement.Origination.IntegrationTests;

/// <summary>
/// The local status-projection logic lives on the aggregate (updated by idempotent
/// consumers). These verify the monotonic, no-regression status transitions that keep the
/// applicant's status view correct under duplicate / out-of-order event delivery.
/// </summary>
public class ApplicationStatusTests
{
    private static LoanApplication NewApp() =>
        LoanApplication.Submit(Guid.NewGuid(), "Acme", 3000m, "USD", 12);

    [Fact]
    public void MarkDecisioned_Approved_MovesToDecisioned()
    {
        var app = NewApp();
        app.MarkDecisioned(approved: true);
        app.Status.Should().Be(ApplicationStatus.Decisioned);
    }

    [Fact]
    public void MarkDecisioned_Declined_MovesToRejected()
    {
        var app = NewApp();
        app.MarkDecisioned(approved: false);
        app.Status.Should().Be(ApplicationStatus.Rejected);
    }

    [Fact]
    public void MarkContracted_AfterDecisioned_MovesToContracted()
    {
        var app = NewApp();
        app.MarkDecisioned(approved: true);
        app.MarkContracted();
        app.Status.Should().Be(ApplicationStatus.Contracted);
    }

    [Fact]
    public void MarkDecisioned_AfterContracted_DoesNotRegress()
    {
        var app = NewApp();
        app.MarkDecisioned(approved: true);
        app.MarkContracted();

        // A late / duplicate decision event must not pull the status back.
        app.MarkDecisioned(approved: true);

        app.Status.Should().Be(ApplicationStatus.Contracted, "status must not regress on out-of-order delivery");
    }

    [Fact]
    public void MarkContracted_AfterRejected_Throws()
    {
        var app = NewApp();
        app.MarkDecisioned(approved: false);

        var act = () => app.MarkContracted();

        act.Should().Throw<ApplicationDomainException>();
    }
}
