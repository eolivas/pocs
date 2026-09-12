# Compliance & Reporting

Bounded context: **Audit, regulatory, analytics**. A consumer/projection-only service that consumes the full event stream to build an audit trail, regulatory read models, and analytics/reporting projections (including per-partner reporting via `PartnerId`). No transactional aggregates.

This service is the merge of the originally-requested **Compliance** and **Reporting** services — both are read-heavy, event-consuming projection work sharing the same lifecycle. See the boundary analysis in the [root HLD](../../README.md).

- **Database:** `ComplianceReportingDb` (read models / projections only)
- **Publishes:** nothing (terminal consumer)
- **Consumes:** all domain events

Projections and reporting queries are a later deep-dive.
