using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace LoanManagement.Origination.Architecture.Tests;

/// <summary>
/// Enforces the Clean Architecture dependency rule for the Origination (LOS) service:
///
///   Domain          -> depends on nothing
///   Application     -> depends on Domain only
///   Infrastructure  -> depends on Domain + Application
///   Api             -> depends on all lower layers
///
/// These tests are the template every other service's architecture tests follow.
/// </summary>
public class CleanArchitectureTests
{
    private const string DomainNamespace = "LoanManagement.Origination.Domain";
    private const string ApplicationNamespace = "LoanManagement.Origination.Application";
    private const string InfrastructureNamespace = "LoanManagement.Origination.Infrastructure";
    private const string ApiNamespace = "LoanManagement.Origination.Api";

    private static readonly Assembly DomainAssembly =
        typeof(LoanManagement.Origination.Domain.AssemblyMarker).Assembly;

    private static readonly Assembly ApplicationAssembly =
        typeof(LoanManagement.Origination.Application.DependencyInjection).Assembly;

    private static readonly Assembly InfrastructureAssembly =
        typeof(LoanManagement.Origination.Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void Domain_Should_Not_DependOn_Application_Infrastructure_Or_Api()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .ResideInNamespace(DomainNamespace)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "the Domain layer must not depend on any outer layer. Offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_Should_Not_DependOn_Infrastructure_Or_Api()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespace(ApplicationNamespace)
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNamespace, ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "the Application layer may depend on Domain only. Offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Infrastructure_Should_Not_DependOn_Api()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .ResideInNamespace(InfrastructureNamespace)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "the Infrastructure layer must not depend on the Api layer. Offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? []));
    }
}
