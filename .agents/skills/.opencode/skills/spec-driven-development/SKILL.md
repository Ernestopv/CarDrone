# Spec-Driven Development Skill

## Purpose

Use this skill to implement project features using a Spec-Driven Development workflow.

The specification is the source of truth for implementation.

Do not expand scope beyond the active specification and task.

---

# Core Principle

Follow this order:

```text
PROJECT CONTEXT
↓
SPECIFICATION
↓
CURRENT TASK
↓
IMPLEMENTATION
↓
VALIDATION
↓
REPORT
```

Do not skip directly from a vague request to coding.

---

# Required Project Files

Before implementation, inspect these files when they exist:

```text
AGENTS.md
MEMORY.md
docs/PRD.md
docs/ARCHITECTURE.md
docs/DECISIONS.md
tasks/BACKLOG.md
```

The active task is the `IN_PROGRESS` entry (or the `NEXT` entry when starting
work) in `tasks/BACKLOG.md`; this project does not use `tasks/CURRENT.md`. Also
read every specification referenced by the active task.

Specifications normally live under:

```text
specs/
```

---

# Priority of Instructions

When instructions conflict, use this priority:

1. `AGENTS.md`
2. Active specification (referenced by the active backlog task)
3. Active backlog task in `tasks/BACKLOG.md`
4. `docs/ARCHITECTURE.md`
5. `docs/DECISIONS.md`
6. `docs/PRD.md`
7. `MEMORY.md`
8. Existing implementation conventions

Do not silently resolve important contradictions.

If a contradiction affects implementation, report it clearly and choose the smallest interpretation that does not expand scope.

---

# Before Coding

Before changing files:

1. Read project instructions.
2. Read the active task.
3. Read all referenced specifications.
4. Inspect the existing implementation.
5. Determine which requirements are already satisfied.
6. Identify the minimum set of files that need to change.
7. Reuse existing architecture and components where possible.
8. Avoid rewriting working code unnecessarily.

Do not begin by generating a new architecture if a suitable one already exists.

---

# Specification Rules

Treat the active specification as the implementation contract.

Implement:

- required behavior;
- required states;
- required interfaces;
- required validation;
- required acceptance criteria.

Do not implement:

- features not required by the specification;
- speculative future functionality;
- unrelated refactors;
- optional infrastructure without a current need;
- additional dependencies without justification.

Do not infer new product requirements from implementation convenience.

---

# Scope Control

If the specification says a feature is out of scope, do not implement it.

If a future feature is mentioned only for architectural context, prepare clean boundaries where appropriate but do not implement the future feature.

Example:

```text
Current:
React → MockDroneService

Future:
React → .NET API → Raspberry Pi
```

In this situation, implement the abstraction required for the mock service, but do not create the .NET backend unless the active specification requires it.

---

# Incremental Implementation

Prefer small, verifiable changes.

Recommended order:

```text
types
↓
contracts/interfaces
↓
service/domain logic
↓
integration layer
↓
UI
↓
tests
↓
validation
```

Adapt this order when the existing project structure requires it.

Do not perform large unrelated rewrites.

---

# Existing Code

Existing working code should be preserved whenever possible.

Before replacing code:

- understand why it exists;
- check whether it already satisfies part of the specification;
- prefer refactoring over rebuilding;
- avoid changing public contracts unnecessarily.

Do not delete unrelated functionality.

---

# Type Safety

For TypeScript:

- use explicit domain types;
- avoid `any`;
- reuse existing types;
- avoid duplicated string literals where domain types already exist;
- keep component props typed;
- validate external or simulated input where required.

For C#:

- use strong types;
- enable nullable awareness where configured;
- avoid unnecessary dynamic behavior;
- keep domain concepts explicit.

---

# Architecture Boundaries

Respect project boundaries.

For the Drone Control project, the intended high-level separation is:

```text
React UI
↓
Application / Service abstraction
↓
Implementation
```

Future backend:

```text
React
↓
.NET API
↓
Application layer
↓
Hardware abstraction
↓
Raspberry Pi
↓
GPIO / PWM
```

Do not allow frontend code to know GPIO pin mappings.

Do not allow API controllers to directly manipulate GPIO.

Hardware-specific implementation must remain isolated.

---

# Frontend Rules

When working in the frontend:

- keep presentation components mostly prop-driven;
- centralize domain state;
- avoid unnecessary global state libraries;
- reuse existing components;
- preserve accessibility;
- keep UI states explicit;
- distinguish simulated state from real state.

Do not introduce backend logic into React components.

---

# Backend Rules

When backend work becomes active:

- use ASP.NET Core conventions;
- keep controllers thin;
- place business behavior in application/services;
- isolate infrastructure;
- isolate Raspberry Pi/GPIO access;
- use configuration for hardware-specific values;
- validate commands before hardware interaction.

Do not hardcode GPIO behavior across controllers or services.

---

# Drone Command Model

Frontend and backend communication should use semantic commands.

Allowed conceptual commands include:

```text
forward
backward
left
right
stop
```

Do not expose GPIO pin numbers to the frontend.

Example:

```text
React
↓
FORWARD
↓
Backend
↓
Hardware mapping
↓
GPIO
```

---

# Hardware Safety

Physical drone control must be introduced incrementally.

Never assume successful physical execution without confirmation.

The system should distinguish:

```text
requested command
```

from:

```text
confirmed command
```

When real hardware integration becomes active, connection loss and STOP behavior must receive explicit treatment in the relevant specification.

Do not invent electrical behavior from GPIO numbers alone.

---

# GPIO Configuration

The known planned Raspberry Pi configuration is:

```json
{
  "GPIO": {
    "Pin1": 23,
    "Pin2": 24,
    "Pin3": 21,
    "Pin4": 20,
    "PWM1": 12,
    "PWM2": 13
  }
}
```

Treat this as configuration data.

Do not infer which direction or motor action each pin performs unless that mapping is explicitly specified elsewhere.

---

# Video Architecture

Future video flow is expected to be:

```text
Raspberry Pi Camera
↓
uStreamer
↓
Video stream
↓
React CameraView
```

Do not implement real streaming until an active specification requires it.

Before that, use explicit mock states such as:

```text
offline
connecting
streaming
error
```

---

# Dependencies

Before adding a dependency:

1. Check whether the existing stack already solves the problem.
2. Confirm the specification actually requires the capability.
3. Prefer mature, maintained libraries.
4. Avoid duplicate libraries for the same responsibility.
5. Report newly added dependencies in the final summary.

Do not install libraries merely to simplify a small amount of code.

---

# Testing

Where tests exist, update or add tests for modified behavior.

Where tests do not yet exist, do not introduce an oversized testing framework unless required by the task.

At minimum, validate the acceptance criteria manually or through existing project tooling.

---

# Acceptance Criteria

Every specification should contain acceptance criteria.

Before declaring the task complete:

1. Read every acceptance criterion again.
2. Verify each criterion against the implementation.
3. Do not assume compliance because the build succeeds.
4. Report any criterion that is not satisfied.

Use a checklist internally:

```text
[ ] Criterion 1
[ ] Criterion 2
[ ] Criterion 3
```

A task is not complete if mandatory acceptance criteria remain unmet.

---

# Validation

Use the project's actual scripts.

Inspect `package.json`, solution files, project files, or existing tooling before assuming commands.

For a React project, validation may include:

```bash
npm run build
npm run lint
npm test
```

Only run scripts that actually exist.

For .NET, validation may include:

```bash
dotnet build
dotnet test
```

Use the project's real solution/project paths.

---

# Error Handling

Do not hide errors introduced by the implementation.

Do not disable lint rules or type checking merely to make validation pass.

Fix the underlying issue whenever practical.

If an existing unrelated error prevents validation, report it separately.

---

# Documentation

Update documentation when the implementation changes an established contract, architecture decision, or requirement.

Possible files:

```text
MEMORY.md
docs/ARCHITECTURE.md
docs/DECISIONS.md
tasks/BACKLOG.md
```

Do not update documentation just to repeat code details.

---

# MEMORY.md

Use `MEMORY.md` for durable project context and lessons learned.

Good examples:

- architecture decisions already applied;
- conventions discovered in the codebase;
- important implementation constraints;
- relevant technical decisions;
- known limitations.

Do not use `MEMORY.md` as a duplicate of the PRD or active specification.

Only update it when new durable context was actually learned.

---

# Active Task and Backlog Lifecycle

`tasks/BACKLOG.md` is the project roadmap and the status authority. The active
task is the `IN_PROGRESS` entry, or the `NEXT` entry when starting work. This
project does not use `tasks/CURRENT.md`. Implement only the active task; if it
references a specification, that specification defines the detailed behavior.

Do not implement future backlog items merely because they are nearby or easy.

Find the backlog task that matches the active specification. Before
implementation:

- `NEXT` → change to `IN_PROGRESS`.
- already `IN_PROGRESS` → leave unchanged.
- `COMPLETED` → do not silently reimplement it; report the situation.

Mark the task `COMPLETED` only when every mandatory acceptance criterion has
passed. Do not automatically begin the next item; after completion, one next
`PENDING` task may be changed to `NEXT` (never to `IN_PROGRESS`), and it must
not be implemented or given a specification automatically.

Real hardware validation does not block completion unless the active
specification explicitly makes physical verification a mandatory criterion.
Software implementation, builds, tests, mock/dry-run validation, and static
platform checks satisfy the task when physical validation is intentionally
deferred to a later end-to-end task.

`NOT VERIFIED` is not `BLOCKED`: deferred hardware checks are recorded as
`NOT VERIFIED`. `BLOCKED` is reserved for a genuine missing prerequisite that
prevents implementation from continuing (for example, unavailable hardware,
missing confirmed wiring or device permissions, missing electrical
information) — never merely because real-hardware validation has not yet been
performed.

---

# Completion Workflow

Before finishing:

1. Re-read the active specification.
2. Re-read the acceptance criteria.
3. Review changed files.
4. Run build/type checks.
5. Run lint.
6. Run tests if available.
7. Fix issues introduced by the task.
8. Confirm no out-of-scope functionality was added.
9. Summarize the result.

---

# Final Report Format

At the end of the task, report:

## Implemented

Briefly explain what was implemented.

## Specification

Path of the implemented specification.

## Skills Used

List the relevant skills actually used (for example
`spec-driven-development`, `dotnet`, `docker`, `raspberry-pi`).

## Files Created

List created files.

If none:

```text
None
```

## Files Modified

List modified files.

If none:

```text
None
```

## Architecture

Important implementation decisions.

## Acceptance Criteria

Report every criterion individually as:

```text
PASS — ...
FAIL — ...
NOT VERIFIED — ...
```

Do not omit criteria, and do not mark a criterion `PASS` unless it was
actually verified.

## Validation

Use only relevant entries, each `PASS / FAIL / NOT RUN`:

```text
Restore
Build
Lint
Tests
Docker Build
Compose Validation
Runtime Verification
Multi-Arch Build
Raspberry Pi Runtime
Dry-Run Hardware
Real GPIO
Real PWM
Real Camera
```

## Hardware Verification

For hardware-related tasks, report the highest level actually verified:

```text
Not applicable
Mock only
Dry-run only
Raspberry Pi runtime verified
Real hardware verified
Not verified
```

Never claim a higher verification level than was actually performed.

## Dependencies

List any new dependencies.

If none:

```text
None
```

## Remaining Issues

List unresolved issues.

If none:

```text
None
```

## Backlog Status

Report:

```text
COMPLETED
BLOCKED
IN_PROGRESS
```

## Next Task

State the next task if known and confirm:

```text
Next task was not started.
```

---

# Prohibited Behavior

Do not:

- invent requirements;
- implement future backlog tasks;
- rebuild working areas without need;
- silently broaden scope;
- add unnecessary dependencies;
- hide compiler or lint errors;
- claim acceptance criteria passed without verification;
- introduce real hardware communication during frontend-only specifications;
- expose GPIO details to React;
- claim a physical command succeeded without confirmation;
- automatically begin the next task.

---

# Success Condition

The skill has been followed correctly when:

```text
specification
    ↓
implementation
    ↓
validation
```

remain aligned, and the implementation contains no meaningful behavior beyond the active specification.
