# POCs — Proofs of Concept for Real-World Engineering Challenges

This repository is a home for proofs of concept (POCs) that bring solutions to real-world software engineering challenges across different business domains.

A POC here can be one of three kinds:

- **Tech approach** — proving out a specific technology, pattern, or integration (e.g., event-driven messaging, CQRS, a caching strategy, an LLM/RAG pipeline).
- **Domain approach** — modeling a specific business domain and its rules (e.g., payments, logistics, scheduling, notifications).
- **Interview approach** — working through system design and coding problems the way you'd tackle them in a senior-level interview.

Each POC is a focused experiment, not a production system. The goal is to prove a thesis, document the tradeoffs, and stop.

## The Approach: System Design Framework

Every POC follows the same working method — the four-step framework from Alex Xu's *System Design Interview*, applied to day-to-day design work.

> The best system design skill isn't drawing the perfect architecture. It's resisting the urge to start.

1. **Understand the problem and scope it.** Ask questions, make explicit assumptions, and gather what you actually need before drawing a single box. Define what's in and out of scope.
2. **Propose a high-level design and get buy-in.** Draw the key components, do back-of-the-envelope estimates to check the design fits the scale, and walk through concrete use cases.
3. **Deep dive.** Prioritize the components that matter. Cover bottlenecks, failure modes, and how you'd monitor it in production — without over-engineering.
4. **Wrap up.** Recap, name the tradeoffs, and cover rollout and how you'd handle the next scale.

The full framework, including how it maps to each POC and the behavioral guardrails we watch for (over-engineering, stubbornness, treating feedback as an attack), lives in [`.kiro/steering/30-system-design-framework.md`](.kiro/steering/30-system-design-framework.md).

## How the Project Is Guided

Work in this repo is guided by a set of steering files under [`.kiro/steering/`](.kiro/steering):

- **[`senior-fullstack-engineer.md`](.kiro/steering/senior-fullstack-engineer.md)** *(always on)* — the lead voice. Provides the technical depth across backend, frontend, mobile, cloud (AWS/Azure), system design, architecture, fundamentals, DS&A, AI/LLM, languages, and DevOps.
- **[`senior-recruiter-expert.md`](.kiro/steering/senior-recruiter-expert.md)** *(manual, brought in when needed)* — the career/interview lens: job analysis, interview-phase prep, and communication coaching.
- **[`30-system-design-framework.md`](.kiro/steering/30-system-design-framework.md)** *(always on)* — the process backbone that defines how each POC is scoped, built, and documented.
- **`01`–`29` numbered files** — implementation conventions (clean architecture, DDD, CQRS, messaging, EF Core, testing, security, scaling, and more), pulled in per POC as relevant.

## POC Structure

Each POC should make its thinking visible, not just its code. Alongside the implementation, capture the four framework steps:

1. **Problem & scope** — questions asked, assumptions made, explicit in/out of scope.
2. **High-level design** — components, API, data flow, and back-of-envelope estimates.
3. **Deep dive** — the one or two areas the POC actually proves, with bottlenecks and failure modes.
4. **Wrap up** — tradeoffs, what was intentionally left out, and what the next scale looks like.

A suggested layout for a new POC:

```
/<domain-or-topic>-<short-name>/
  README.md        # the four framework steps for this POC
  src/             # the implementation
  ...
```

## Getting Started

1. Pick a challenge and decide which kind of POC it is (tech, domain, or interview).
2. Start with step 1 of the framework — scope it before writing code.
3. Add a POC folder with its own `README.md` documenting the four steps.
4. Build only what proves the thesis. Apply YAGNI to both code and infrastructure.
