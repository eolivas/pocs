# Credit Engine

Bounded context: **Underwriting decisions**. Evaluates a submitted application and produces a decision (approve / decline / refer) with terms. Owns the `CreditDecision` aggregate.

- **Database:** `CreditDb`
- **Publishes:** `CreditDecisionMade`, `CreditDecisionReferred`
- **Consumes:** `LoanApplicationSubmitted`

See the [root HLD](../../README.md) for the full design. Domain logic is a later deep-dive.
