---
description: Plan a backlog task and create its SDD specification without implementing code
---

Follow the `spec-driven-development` skill. It owns the shared SDD workflow
(instruction priority, hardware verification levels, NOT VERIFIED vs BLOCKED,
backlog lifecycle). This command owns the planning-specific rules below.

The task to plan is:

$ARGUMENTS

Read before planning (prefer only these — do not load every spec):

- `AGENTS.md`
- `MEMORY.md`
- `tasks/BACKLOG.md` (the requested task and its completed predecessors)
- the requested task's field "Specification:" (existing spec, if any)
- `docs/DECISIONS.md` and `docs/ARCHITECTURE.md` when decisions or boundaries apply
- specifications directly related to the task
- relevant existing source code only to understand the current architecture

# Objective

Plan the requested task using Spec-Driven Development: create or update the
appropriate specification. Do NOT implement application code. Do NOT modify
application source code. Do NOT start the task implementation.

# Source of Truth

Use this priority:

1. `AGENTS.md`
2. Existing approved specifications
3. `tasks/BACKLOG.md`
4. `docs/ARCHITECTURE.md`
5. `docs/DECISIONS.md`
6. `docs/PRD.md`
7. `MEMORY.md`
8. Existing implementation conventions

The generated specification defines WHAT must eventually be implemented.
Specialized skills help determine HOW. A specialized skill must never expand
the task beyond the specification.

# Hardware planning rules

For hardware-related tasks: use only explicitly documented hardware behavior.
Never guess pin semantics, wiring, HIGH/LOW mappings, PWM frequencies, duty
cycles, electrical limits, device paths, or camera capabilities. Missing
physical information is recorded in the specification as a prerequisite, an
assumption requiring verification, or an open question — never invented.

# Specification location

Choose the folder by primary responsibility:

```text
specs/frontend/<feature-name>.md
specs/backend/<feature-name>.md
specs/docker/<feature-name>.md
specs/integration/<feature-name>.md
specs/hardware/<feature-name>.md
specs/architecture/<feature-name>.md
specs/deployment/<feature-name>.md
```

Use kebab-case filenames. A spec may depend on specs from other domains; do
not duplicate another specification because multiple technologies are involved.

# Required specification structure

Use the following sections when relevant:

```markdown
# Feature Name

## Purpose
## Dependencies
## Scope
## Out of Scope
## Architecture
## Domain Model
## Behavior
## Interfaces
## Validation
## Error Cases
## Platform Requirements
## Security / Safety
## Testing Scenarios
## Acceptance Criteria
```

Do not create empty sections merely to satisfy the template.

# Acceptance criteria quality

Criteria must be objective, testable, observable, and scoped to the active
task. Prefer concrete assertions such as `GET /api/health returns HTTP 200.`
or `The Docker image builds for linux/arm64.` over aspirational wording such
as `The architecture should be clean.`

For hardware-related tasks, criteria must never imply physical verification
that has not been performed; keep the verification ladder semantics
(implemented → built → mock → dry-run → Raspberry Pi → real hardware).

# Scope control

Do not include future backlog tasks simply because they are related. Future
work may be referenced only to define appropriate boundaries.

# Architecture boundaries

Preserve the CarDrone layering:

```text
React → frontend service abstraction → ASP.NET Core API → Application →
hardware abstraction → Raspberry Pi / GPIO
```

Rules: React must not know GPIO pin mappings; API controllers must not
manipulate GPIO directly; Domain must not depend on Infrastructure;
hardware-specific code stays isolated; containerization must not leak into
Domain logic. Full rules: `AGENTS.md` and the skill.

# Backlog handling

Planning does not start implementation. Preserve the task status (`PENDING`
or `NEXT`); do not mark the task `COMPLETED`; do not change it to
`IN_PROGRESS`; do not automatically plan the next backlog task.

# Final report

After planning, report:

## Specification Created

Path to the created or updated specification.

## Task

Backlog task that was planned.

## Skills Used

List relevant skills actually used.

## Dependencies

Important dependencies on existing specs or implementation.

## Main Decisions

Important architectural decisions introduced by the specification.

## Assumptions

If none: `None`.

## Open Questions

Only questions that materially affect future implementation. If none: `None`.

## Hardware Verification

For hardware-related tasks, report the highest level that applies:

```text
Not applicable
Mock only
Dry-run only
Raspberry Pi runtime verified
Real hardware verified
Not verified
```

Do not claim a higher verification level than was actually performed.

## Implementation

Confirm:

```text
No application code was implemented.
```

Do not proceed to implementation automatically.