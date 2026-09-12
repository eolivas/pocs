# OCP — Open/Closed Principle (Credit Engine)

> "Open for extension, closed for modification."

**Service:** Credit Engine — underwriting decisions (owns `CreditDecision`, consumes `LoanApplicationSubmitted`, publishes `CreditDecisionMade`).
**Status in repo:** Credit Engine is scaffold-only today (just `AssemblyMarker`), so this is the *designed* shape for its later deep-dive — it follows the exact conventions of the implemented services (sealed types, file-scoped namespaces, `DomainEvent`, strongly-typed IDs).

---

## Why it matters here

Underwriting is a moving target. Today the rule set is "affordability + minimum amount"; next
quarter it's "add a partner-risk band," then "add a bureau-score cutoff." If every new rule forces
an edit to the one class that makes the decision, that class becomes a merge-conflict magnet and
every change re-risks the whole decision path.

OCP says: model each rule as its own class behind a small abstraction, and let the decision
**engine iterate over whatever rules are registered**. Adding a rule = adding a class + one DI line.
The engine and the existing rules are never touched. This mirrors the steering doc's OCP guidance
("extend via new classes… prefer composition over inheritance") and the way the real system adds
MassTransit consumers and pipeline stages without editing existing ones.

## The abstraction

```csharp
namespace LoanManagement.CreditEngine.Domain.Scoring;

/// <summary>The application facts a rule evaluates. IDs + minimal data, mirroring event payloads.</summary>
public sealed record CreditApplication(
    Guid ApplicationId,
    decimal RequestedAmount,
    string Currency,
    int TermMonths,
    decimal MonthlyIncome,
    decimal MonthlyDebt,
    int BureauScore);

/// <summary>Outcome of a single rule. Rules only *contribute*; they never make the final call.</summary>
public sealed record RuleResult(bool Passed, string Reason)
{
    public static RuleResult Pass(string reason) => new(true, reason);
    public static RuleResult Fail(string reason) => new(false, reason);
}

/// <summary>
/// One underwriting rule = one class = one reason to change. New rules are new
/// implementations; the engine that runs them is closed for modification.
/// </summary>
public interface ICreditRule
{
    RuleResult Evaluate(CreditApplication application);
}
```

## The engine — closed for modification

```csharp
namespace LoanManagement.CreditEngine.Domain.Scoring;

/// <summary>
/// Runs every registered rule and folds the results into a decision. This class does NOT
/// change when the rule set changes — new behaviour arrives as new ICreditRule classes.
/// </summary>
public sealed class CreditPolicy(IEnumerable<ICreditRule> rules)
{
    public CreditAssessment Assess(CreditApplication application)
    {
        var results = rules.Select(r => r.Evaluate(application)).ToList();
        var failures = results.Where(r => !r.Passed).Select(r => r.Reason).ToList();

        var outcome = failures.Count == 0
            ? CreditOutcome.Approved
            : CreditOutcome.Declined;

        return new CreditAssessment(outcome, failures);
    }
}

public enum CreditOutcome { Approved, Declined, Referred }

public sealed record CreditAssessment(CreditOutcome Outcome, IReadOnlyList<string> Reasons);
```

## Extension = new classes only

```csharp
namespace LoanManagement.CreditEngine.Domain.Scoring.Rules;

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

/// <summary>Bureau score floor. Added LATER — the engine and AffordabilityRule are untouched.</summary>
public sealed class BureauScoreRule : ICreditRule
{
    private const int MinScore = 620;

    public RuleResult Evaluate(CreditApplication a) =>
        a.BureauScore >= MinScore
            ? RuleResult.Pass($"Score {a.BureauScore} \u2265 {MinScore}.")
            : RuleResult.Fail($"Score {a.BureauScore} below floor {MinScore}.");
}
```

Wiring the new rule is one additive line in the composition root — no existing file changes:

```csharp
// CreditEngine DependencyInjection.cs (Application/Infrastructure)
services.AddSingleton<ICreditRule, AffordabilityRule>();
services.AddSingleton<ICreditRule, BureauScoreRule>();   // <-- the ONLY edit to add a rule
services.AddSingleton<CreditPolicy>();
```

The MediatR handler that reacts to `LoanApplicationSubmitted` just asks the policy — it never
grows an `if` per rule:

```csharp
public sealed class EvaluateApplicationHandler(CreditPolicy policy, ICreditDecisionRepository repo)
    : IRequestHandler<EvaluateApplicationCommand>
{
    public async Task Handle(EvaluateApplicationCommand cmd, CancellationToken ct)
    {
        var assessment = policy.Assess(cmd.Application);          // engine folds all rules
        var decision = CreditDecision.Record(cmd.Application.ApplicationId, assessment); // raises CreditDecisionMade
        await repo.AddAsync(decision, ct);
        await repo.SaveChangesAsync(ct);
    }
}
```

---

## Anti-pattern (violates OCP)

```csharp
// BAD — every new rule reopens this method. Growing if-chain, re-tested end to end each time.
public CreditOutcome Decide(CreditApplication a)
{
    if (a.MonthlyIncome <= 0) return CreditOutcome.Declined;
    if (a.MonthlyDebt / a.MonthlyIncome > 0.40m) return CreditOutcome.Declined;
    // 3 weeks later, edit again:
    if (a.BureauScore < 620) return CreditOutcome.Declined;
    // and again... this class never stops changing
    return CreditOutcome.Approved;
}
```

Each policy change edits shared code, so unrelated rules risk regression and the class violates
both OCP and SRP at once.

## Interview angle

Say: *"Underwriting rules churn, so I model each rule as an `ICreditRule` and have the engine fold
over whatever's registered. Adding a bureau-score cutoff is a new class plus one DI registration —
the engine and the existing rules are closed for modification. This is the same move the codebase
already uses for MassTransit consumers: new reactions are new `IConsumer<T>` classes, never edits to
the publisher."*

**Common follow-ups:**
- *How does the engine decide Referred vs Declined?* → give `RuleResult` a severity, or split `IHardRule`/`ISoftRule`; still additive.
- *Ordering / short-circuit?* → add an `Order` property or a decorator that stops on first hard fail — again, no engine edit.
- *Config-driven thresholds?* → inject thresholds via `IOptions<>` so even a threshold change avoids code edits.

**Pitfalls:**
- Faking OCP with an `enum` + `switch` — that's still modification. The extension point must be a type, not a branch.
- Over-abstracting a rule set that genuinely never changes (YAGNI). OCP earns its keep only where change is real — underwriting qualifies.
