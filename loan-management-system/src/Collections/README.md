# Collections / Recovery

Bounded context: **Delinquency & recovery**. Opens and manages a delinquency case when payments are missed and drives the recovery workflow. Owns the `DelinquencyCase` aggregate.

- **Database:** `CollectionsDb`
- **Publishes:** `DelinquencyCaseOpened`, `CaseEscalated`, `CaseResolved`
- **Consumes:** `PaymentMissed`, `PaymentApplied`, `LoanPaidOff`

See the [root HLD](../../README.md) for the full design. Domain logic is a later deep-dive.
