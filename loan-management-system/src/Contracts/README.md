# Contracts

Bounded context: **Loan agreements**. Generates the agreement for an approved application, captures signature, and manages expiry. Owns the `Contract` aggregate.

- **Database:** `ContractsDb`
- **Publishes:** `ContractGenerated`, `ContractSigned`, `ContractExpired`, `ContractVoided`
- **Consumes:** `CreditDecisionMade`

See the [root HLD](../../README.md) for the full design. Domain logic is a later deep-dive.
