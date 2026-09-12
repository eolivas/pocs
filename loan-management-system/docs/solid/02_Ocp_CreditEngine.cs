// =============================================================================
// OCP — Open/Closed Principle (Credit Engine service)
// "Open for extension, closed for modification."
//
// STUDY FILE — self-contained, NOT compiled into the build.
// Credit Engine is scaffold-only today (just AssemblyMarker), so this is the
// DESIGNED shape for its later deep-dive. Underwriting rules churn constantly;
// modelling each as a class behind ICreditRule means a new rule is a NEW CLASS
// plus one DI line — the engine and existing rules are never edited.
//
// Same move the real codebase already uses for MassTransit consumers: a new
// reaction is a new IConsumer<T>, never an edit to the publisher.
// =============================================================================

namespace LoanManagement.Study.Solid.Ocp;

// -----------------------------------------------------------------------------
// The abstraction: one rule = one class = one reason to change.
// -----------------------------------------------------------------------------
public sealed record CreditApplication(
    Guid ApplicationId,
    decimal RequestedAmount,
    string Currency,
    int TermMonths,
    decimal MonthlyIncome,
    decimal MonthlyDebt,
    int BureauScore);

public sealed record RuleResult(bool Passed, string Reason)
{
    public static RuleResult Pass(string reason) => new(true, reason);
    public static RuleResult Fail(string reason) => new(false, reason);
}

/// <summary>One underwriting rule. New rules are new implementations of THIS.</summary>
public interface ICreditRule
{
    RuleResult Evaluate(CreditApplication application);
}

// -----------------------------------------------------------------------------
// The engine — CLOSED for modification. It folds over whatever rules are
// registered; it never changes when the rule set grows.
// -----------------------------------------------------------------------------
public enum CreditOutcome { Approved, Declined, Referred }

public sealed record CreditAssessment(CreditOutcome Outcome, IReadOnlyList<string> Reasons);

public sealed class CreditPolicy(IEnumerable<ICreditRule> rules)
{
    public CreditAssessment Assess(CreditApplication application)
    {
        var results = rules.Select(r => r.Evaluate(application)).ToList();
        var failures = results.Where(r => !r.Passed).Select(r => r.Reason).ToList();

        var outcome = failures.Count == 0 ? CreditOutcome.Approved : CreditOutcome.Declined;
        return new CreditAssessment(outcome, failures);
    }
}

// -----------------------------------------------------------------------------
// EXTENSION = new classes only. The engine above is untouched by any of these.
// -----------------------------------------------------------------------------

/// <summary>Debt-to-income must stay under the affordability ceiling.</summary>
public sealed class AffordabilityRule : ICreditRule
{
    private const decimal MaxDebtToIncome = 0.40m;

    public RuleResult Evaluate(CreditApplication a)
    {
        if (a.MonthlyIncome <= 0) return RuleResult.Fail("No verifiable income.");
        var dti = a.MonthlyDebt / a.MonthlyIncome;
        return dti <= MaxDebtToIncome
            ? RuleResult.Pass($"DTI {dti:P0} within limit.")
            : RuleResult.Fail($"DTI {dti:P0} exceeds {MaxDebtToIncome:P0}.");
    }
}

/// <summary>Bureau-score floor. Added LATER — engine + AffordabilityRule stay closed.</summary>
public sealed class BureauScoreRule : ICreditRule
{
    private const int MinScore = 620;

    public RuleResult Evaluate(CreditApplication a) =>
        a.BureauScore >= MinScore
            ? RuleResult.Pass($"Score {a.BureauScore} >= {MinScore}.")
            : RuleResult.Fail($"Score {a.BureauScore} below floor {MinScore}.");
}

// -----------------------------------------------------------------------------
// Wiring — the ONLY edit needed to add a rule is one additive DI line.
// (Illustrative composition-root snippet; real services wire this in DependencyInjection.cs.)
// -----------------------------------------------------------------------------
public static class CreditEngineRegistration
{
    public static void AddCreditScoring(ISimpleServiceCollection services)
    {
        services.AddSingleton<ICreditRule, AffordabilityRule>();
        services.AddSingleton<ICreditRule, BureauScoreRule>(); // <-- adding a rule = this line only
        services.AddSingleton<CreditPolicy>();
    }
}

// -----------------------------------------------------------------------------
// The handler asks the policy — it never grows an `if` per rule.
// -----------------------------------------------------------------------------
public sealed class EvaluateApplicationHandler(CreditPolicy policy, ICreditDecisionRepository repo)
{
    public async Task Handle(CreditApplication application, CancellationToken ct)
    {
        var assessment = policy.Assess(application);           // engine folds ALL rules
        var decision = CreditDecision.Record(application.ApplicationId, assessment);
        await repo.AddAsync(decision, ct);
        await repo.SaveChangesAsync(ct);
    }
}

// -----------------------------------------------------------------------------
// ANTI-PATTERN — every new rule REOPENS this method. Growing if-chain; unrelated
// rules risk regression on each edit. Violates OCP (and SRP) at once.
// -----------------------------------------------------------------------------
public sealed class BadCreditDecider
{
    public CreditOutcome Decide(CreditApplication a)
    {
        if (a.MonthlyIncome <= 0) return CreditOutcome.Declined;
        if (a.MonthlyDebt / a.MonthlyIncome > 0.40m) return CreditOutcome.Declined;
        // 3 weeks later, edit again:
        if (a.BureauScore < 620) return CreditOutcome.Declined;
        // ...and again. This class never stops changing.
        return CreditOutcome.Approved;
    }
}

// =============================================================================
// Self-contained supporting types (mirror the designed CreditDecision aggregate
// + repository conventions used across the implemented services).
// =============================================================================
public sealed class CreditDecision
{
    public Guid ApplicationId { get; private init; }
    public CreditOutcome Outcome { get; private init; }
    private CreditDecision() { }
    public static CreditDecision Record(Guid applicationId, CreditAssessment assessment)
        => new() { ApplicationId = applicationId, Outcome = assessment.Outcome };
}

public interface ICreditDecisionRepository
{
    Task AddAsync(CreditDecision decision, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

// Minimal DI surface so the wiring snippet reads like the real IServiceCollection.
public interface ISimpleServiceCollection
{
    void AddSingleton<TService, TImpl>() where TImpl : TService;
    void AddSingleton<TService>();
}
