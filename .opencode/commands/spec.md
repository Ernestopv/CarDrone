---
description: Implement and validate an existing specification using the SDD workflow
---

Follow the `spec-driven-development` skill. It owns the SDD workflow: what to
read, instruction priority, scope rules, validation honesty, the backlog
lifecycle (`NEXT` → `IN_PROGRESS` → `COMPLETED`, `PENDING` → `NEXT` promotion,
`NOT VERIFIED` vs `BLOCKED`), hardware verification levels, and the final
report format. This command only selects the target and its context.

The specification to implement is:

$ARGUMENTS

Read before making changes (prefer only these — do not load unrelated areas):

- `AGENTS.md`
- `MEMORY.md`
- `tasks/BACKLOG.md` (active task)
- the specification in `$ARGUMENTS`
- specifications it directly references
- `docs/DECISIONS.md` and `docs/ARCHITECTURE.md` when the task depends on them
- `docs/PRD.md`, `docs/hardware/WIRING.md`, `docs/hardware/raspberry-pi-inventory.md`
  only when the task requires them

Inspect the existing implementation before writing code.

# Objective

Implement only:

```text
$ARGUMENTS
```

The specification is the source of truth.

Do not expand scope.

Do not automatically implement another backlog task.

The specification defines WHAT; specialized skills define HOW; a specialized
skill must never expand WHAT.

# Skill selection

Always use `spec-driven-development`. Add `dotnet`, `docker`, `raspberry-pi`,
or a frontend skill only when the specification's domain genuinely requires
them. Choose the minimum set. Never choose only one skill when the active
specification crosses multiple domains.

# Scope enforcement

Do not implement adjacent backlog tasks, speculative future functionality,
unrelated refactors, or additional infrastructure/dependencies unless the
active specification requires them.

# Project rules to preserve

- Runtime modes: `HARDWARE_MODE = mock | dry-run | real`,
  `CAMERA_MODE = mock | ustreamer`.
- Same source tree for PC and Raspberry Pi (`docker compose up` in both).
- Follow only documented hardware behavior; never guess GPIO/pin/wiring/PWM/
  device-path/camera facts; never claim physical verification not performed.
- Hardware safety rules and NOT VERIFIED/BLOCKED semantics: per the skill.

# Backlog state and report

Per the skill's Backlog Lifecycle: mark the matching task `IN_PROGRESS`
before implementation; mark it `COMPLETED` only when every mandatory
acceptance criterion passed; promote at most one `PENDING` task to `NEXT`
(mark it `NEXT`, not `IN_PROGRESS`).

Finish with the skill's Final Report Format, including:

- the Specification path (`$ARGUMENTS`);
- every acceptance criterion individually as `PASS` / `FAIL` / `NOT VERIFIED`;
- the relevant Validation entries;
- the Hardware Verification level actually achieved;
- Dependencies, Remaining Issues, Backlog Status, and the confirmation:

```text
Next task was not started.
```