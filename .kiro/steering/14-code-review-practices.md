---
inclusion: manual
---

# Code Review Best Practices

This document defines how code reviews are conducted on this project. Reviews are a quality gate, a knowledge-sharing tool, and a mentoring opportunity.

## Review Turnaround

| Priority | Response Time | Context |
|----------|---------------|---------|
| Normal PR | < 24 hours | Standard feature or fix |
| Hotfix PR | < 4 hours | Production incident mitigation |
| Draft PR | Best-effort | Early feedback requested, not blocking |

If you cannot review within the expected window, reassign or comment that you'll review later with a time estimate.

## Comment Categories

Use prefixes to signal intent clearly:

| Prefix | Meaning | Blocking? |
|--------|---------|:---------:|
| `blocker:` | Must be fixed before merge — correctness, security, data loss risk | Yes |
| `suggestion:` | Improvement idea — cleaner approach, better naming, performance | No |
| `question:` | Clarification needed — intent unclear, missing context | Maybe |
| `nit:` | Style or formatting — trivial preference, take-it-or-leave-it | No |
| `praise:` | Something done well — reinforce good patterns | No |

Rules:
- Every review MUST have at least one constructive comment (even if it's praise)
- Non-blocking comments MUST NOT prevent merge if the author acknowledges them
- Blocking comments MUST cite a specific rule, principle, or risk

## What Reviewers Check

### Architecture & Design

- [ ] Code is placed in the correct layer (Domain, Application, Infrastructure, Api)
- [ ] Dependencies point inward only (no domain → infrastructure references)
- [ ] New interfaces are defined in the correct layer
- [ ] SOLID principles are respected (see `12-solid-principles.md`)
- [ ] Appropriate design patterns are used (see `13-design-patterns.md`)

### .NET Backend

- [ ] Domain entities use private setters, factory methods, and raise domain events
- [ ] Commands and queries are separated — no handler does both
- [ ] Validators exist for every new command
- [ ] Repository methods return aggregates fully loaded (`.Include()` where needed)
- [ ] No raw SQL string concatenation — all queries are parameterized
- [ ] Async methods use `CancellationToken` and propagate it
- [ ] `sealed` on classes that should not be inherited (consumers, configurations, handlers)
- [ ] Strongly-typed IDs used for all entity identifiers
- [ ] Exceptions are domain-specific (`{Entity}DomainException`), not generic `Exception`
- [ ] No `Task.Result` or `.Wait()` — async all the way
- [ ] Logging uses structured message templates, not string interpolation
- [ ] New NuGet packages are pinned to exact versions

### React Frontend

- [ ] Components use named exports (no default exports)
- [ ] Types are defined in `types.ts` within the feature module
- [ ] API calls use TanStack Query hooks (`useQuery`, `useMutation`)
- [ ] Server state uses TanStack Query, client state uses Zustand
- [ ] Error states render with `role="alert"` for accessibility
- [ ] Forms use `htmlFor` on labels, `id` on inputs
- [ ] Loading states use `aria-busy="true"`
- [ ] No `any` types — all parameters and return values are typed
- [ ] Barrel exports (`index.ts`) are updated for new public modules
- [ ] `useApiError` hook is used for ProblemDetails parsing

### Testing

- [ ] New features include unit tests (domain) and/or handler tests (application)
- [ ] Property-based tests are added for universal invariants
- [ ] Test naming follows `{Scenario}_{ExpectedBehavior}` convention
- [ ] Mocks verify interactions with `Times.Once()` / `Times.Never()`
- [ ] No test depends on execution order or shared mutable state
- [ ] Frontend tests use `@testing-library/react` with accessible queries (`getByRole`, `getByLabelText`)

### Security

- [ ] No secrets, credentials, or connection strings in code
- [ ] Endpoints use `RequireAuthorization()` unless explicitly public
- [ ] User input is validated (FluentValidation for commands, model binding for endpoints)
- [ ] No `[AllowAnonymous]` without explicit justification in PR description
- [ ] Rate limiting applied to public-facing endpoint groups

### Performance

- [ ] No N+1 query patterns (verify `.Include()` or projections)
- [ ] `AsNoTracking()` used for read-only queries
- [ ] No blocking calls (`Task.Result`, `Thread.Sleep`) in async paths
- [ ] Large collections use pagination, not unbounded `ToListAsync()`

### Documentation

- [ ] Breaking changes documented in CHANGELOG.md
- [ ] New configuration options documented in relevant steering file or README
- [ ] Complex logic has a brief inline comment explaining *why* (not what)

## Author Responsibilities

Before requesting review:

1. Self-review your own diff — catch obvious issues first
2. Ensure CI is green (tests pass, architecture tests pass, linting clean)
3. PR description explains *what* changed and *why*
4. PR diff is ≤ 400 lines — split larger changes into stacked PRs
5. Mark TODO/FIXME comments with a linked issue number

## Reviewer Responsibilities

1. Review the intent first — does the approach make sense for the problem?
2. Check the tests — do they cover the happy path and edge cases?
3. Verify layer placement — is the code in the right project?
4. Look for missing error handling — what happens when things fail?
5. Approve promptly when satisfied — don't block on nits

## Resolving Disagreements

1. Author and reviewer discuss in PR comments
2. If unresolved after 2 rounds, bring in a third reviewer
3. Architecture decisions defer to existing ADRs and steering files
4. If no ADR covers the topic, create one as part of the PR

## Stale Review Policy

- Reviews are automatically dismissed when new commits are pushed (branch protection rule)
- After force-push or significant rework, re-request review explicitly
- Approved PRs should be merged within 24 hours to avoid staleness
