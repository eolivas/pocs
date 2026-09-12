---
inclusion: manual
---

# Conventional Commits & PR Standards

## Commit Message Format

```
<type>(<scope>): <description>
```

- **type** — category of change (see table)
- **scope** — area of codebase affected (e.g., `{entity}`, `money`, `deps`, `frontend`)
- **description** — imperative mood, lowercase, no trailing period

### Commit Types

| Type       | When to use                                |
|------------|-------------------------------------------|
| `feat`     | New feature or user-facing capability      |
| `fix`      | Bug fix                                    |
| `docs`     | Documentation-only changes                 |
| `style`    | Formatting, whitespace (no logic change)   |
| `refactor` | Code restructuring without behavior change |
| `perf`     | Performance improvement                    |
| `test`     | Adding or updating tests                   |
| `chore`    | Tooling, CI, dependencies, maintenance     |
| `ci`       | CI/CD pipeline changes                     |
| `build`    | Build system or external dependency changes|

### Examples

```
feat({entity}): add cancellation endpoint
fix(money): handle zero-amount edge case
chore(deps): update EF Core to 8.0.x
docs(adr): add ADR-006 for caching strategy
test(domain): add property tests for Money addition
feat(frontend): add invoice list page
```

### Breaking Changes

Include a `BREAKING CHANGE:` footer in the commit body:

```
feat({entity}): change {entity} ID from int to UUID

BREAKING CHANGE: {Entity} IDs are now UUIDs. All API consumers must update
their client code to send and receive string-based IDs instead of integers.
```

Optionally append `!` after the scope:
```
feat({entity})!: change {entity} ID from int to UUID
```

## PR Standards

### Title Format
- Under 70 characters
- Follow the same `type(scope): description` format as commits

### PR Template Structure

The repo uses `.github/pull_request_template.md`:

```markdown
# Summary
<!-- Brief description of changes -->

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Refactoring
- [ ] Documentation
- [ ] Dependency update

## Checklist
- [ ] Relevant tests added or updated and passing
- [ ] Architecture tests passing
- [ ] No secrets or credentials committed
- [ ] Breaking changes documented in CHANGELOG.md
- [ ] PR diff ≤ 400 lines
```

### PR Rules
- Keep diffs ≤ 400 lines (split larger changes into multiple PRs)
- All tests must pass (including architecture tests)
- No secrets or credentials in code
- Breaking changes must update CHANGELOG.md
- Push to a feature branch, never directly to main
