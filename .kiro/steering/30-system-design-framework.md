---
inclusion: always
---

# System Design Framework: The Guiding Approach for POCs

This steering file defines the working method for every POC in this repository. It is the process backbone: `senior-fullstack-engineer.md` supplies the technical depth, `senior-recruiter-expert.md` (manual) supplies the interview/career lens, and the numbered steering files (`01`–`29`) supply implementation conventions. This file governs *how we approach* each POC before and while we build it.

The framework comes from Alex Xu's *System Design Interview*. The stance below is our own.

## Core Principle

> The best system design skill isn't drawing the perfect architecture. It's resisting the urge to start.

Our impulse as engineers is to jump straight to a solution. On any non-trivial design we fight that impulse, slow down, and make sure we actually understand the problem first. Real-world systems are complicated. There is no perfect answer, and pretending there is one is how you end up rebuilding it later.

This is not an interview trick. It is how we approach design work day to day, and it is how POCs in this repo are scoped, built, and documented.

## The Four Steps

### 1. Understand the problem and scope it

Don't rush to a solution. Ask questions. Make explicit assumptions. Gather what you actually need before drawing a single box. Good questions are a senior skill, not a stalling tactic.

- Separate functional from non-functional requirements.
- State constraints and assumptions explicitly (traffic, data volume, latency, consistency, budget).
- Define what is in scope for this POC and — just as important — what is out of scope.

### 2. Propose a high-level design and get buy-in

Draw the key components. Do back-of-the-envelope estimations to check the design fits the scale. Walk through concrete use cases out loud. Design is a collaboration with the team and stakeholders, not a solo act.

- Identify major components and their responsibilities.
- Sketch the API surface and data flow.
- Run capacity estimates (QPS, storage, bandwidth) to sanity-check the shape.
- Validate against 1–2 concrete end-to-end use cases.

### 3. Deep dive

Prioritize the components that matter. Resist the urge to over-engineer — that's where people lose the plot, chasing design purity over tradeoffs. Talk through bottlenecks, failure modes, and how you'll operate it in production.

- Go deep only on the components central to the POC's thesis.
- Name bottlenecks and how you'd relieve them.
- Cover failure modes: server down, network loss, partial failure, retries.
- State how you'd monitor it: metrics, logs, traces, alerts.

### 4. Wrap up

Recap. Name the tradeoffs you made. Cover rollout and how you'll handle the next scale.

- Summarize the design and the decisions behind it.
- List tradeoffs explicitly — nothing in engineering is free.
- Describe rollout strategy and the trigger metrics for the next scaling stage (see `28-scaling-system-design.md`).

## How This Applies to a POC in This Repo

Every POC should make its thinking visible, not just its code. Before or alongside the implementation, capture:

1. **Problem & scope** — the questions asked, assumptions made, and explicit in/out of scope.
2. **High-level design** — components, API, data flow, and back-of-envelope estimates.
3. **Deep dive** — the one or two areas the POC actually proves, with bottlenecks and failure modes.
4. **Wrap up** — tradeoffs, what was intentionally left out, and what the next scale looks like.

A POC is a focused experiment, not a production system. Prove the thesis, document the tradeoffs, and stop. Apply YAGNI to both code and infrastructure.

## Behavioral Guardrails

The technical skill gets you in the room. What sets senior engineers apart is collaboration, handling ambiguity, and defending a decision without getting defensive.

Watch for these red flags — in the work and in ourselves:

- **Over-engineering** — building for scale or flexibility the POC doesn't need.
- **Stubbornness** — clinging to the first idea after evidence points elsewhere.
- **Treating feedback as an attack** — feedback is design input, not a verdict.

When scoping is genuinely ambiguous, ask clarifying questions before building. State assumptions explicitly rather than guessing silently.
