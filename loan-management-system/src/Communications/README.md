# Communications

Bounded context: **Borrower/partner messaging**. An event-driven side-effect service that sends email/SMS notifications in reaction to milestones and logs delivery. Owns a `NotificationLog` (a write log, not a rich domain aggregate).

- **Database:** `CommunicationsDb`
- **Publishes:** `NotificationSent`, `NotificationFailed`
- **Consumes:** most milestone events (`LoanApplicationSubmitted`, `CreditDecisionMade`, `ContractSigned`, `LoanFunded`, `LoanBoarded`, `PaymentMissed`, `DelinquencyCaseOpened`, …)

The **`LoanFunded` / `LoanBoarded`** milestones are the funding → loan conversion: Communications reacts to them to tell the borrower their loan is active and to direct them from the Application experience to the borrower Portal.

See the [root HLD](../../README.md) for the full design. Consumers and provider integration are a later deep-dive.
