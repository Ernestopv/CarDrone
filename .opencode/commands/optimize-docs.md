Optimize the Markdown documentation of this project using the previous audit
or by performing a brief audit first if necessary.

Do not implement application code.
Do not modify source code.
Do not start any backlog task.
Do not change runtime behavior.
Do not weaken hardware safety rules.

Main goal:

Reduce token usage and documentation duplication while preserving the current
Spec-Driven Development workflow.

Review and optimize:

- AGENTS.md
- MEMORY.md if present
- README.md
- docs/\*_/_.md
- specs/\*_/_.md
- tasks/BACKLOG.md
- .opencode/commands/\*.md
- .opencode/skills/\*/SKILL.md

Preferred authoritative locations:

AGENTS.md
→ global project and SDD rules

.opencode/skills/spec-driven-development/SKILL.md
→ SDD workflow rules

.opencode/skills/raspberry-pi/SKILL.md
→ Raspberry Pi, GPIO, PWM, hardware validation, NOT VERIFIED/BLOCKED, safety

.opencode/skills/docker/SKILL.md
→ Docker, Compose, multi-architecture rules

.opencode/skills/dotnet/SKILL.md
→ .NET implementation rules

docs/ARCHITECTURE.md
→ stable architecture

docs/DECISIONS.md
→ stable project decisions

tasks/BACKLOG.md
→ concise task definitions and status

specs/\*_/_.md
→ task-specific scope, interfaces, acceptance criteria, and validation only

Optimization rules:

- Remove duplicated global rules from individual specs.
- Replace duplicated wording with references to the authoritative file.
- Keep task-specific acceptance criteria.
- Keep task-specific safety constraints.
- Keep explicit deferred-validation decisions.
- Do not remove important implementation boundaries.
- Do not change semantics unless fixing a clear contradiction.
- Preserve existing file paths and headings when possible.
- Do not rewrite every document from scratch unless necessary.

Optimize /plan-spec so it prefers only:

- AGENTS.md
- requested backlog task
- relevant skills
- directly related architecture/decision docs
- immediately related specs

Do not load every spec unless required.

Optimize /spec so it prefers only:

- AGENTS.md
- active spec
- active backlog task
- relevant skills
- source files needed for the task
- directly required dependency specs

Do not load unrelated project areas.

Before asking questions:

- check docs/DECISIONS.md
- check relevant specs
- check the active task
- check applicable skills

Do not ask questions whose answers are already documented.

Important rules that must remain:

- Tasks are implemented one at a time.
- The next task is never implemented automatically.
- /plan-spec does not implement code.
- /spec implements only the active spec.
- NOT VERIFIED is different from BLOCKED.
- Deferred physical validation does not block completion unless explicitly required.
- BLOCKED is only for genuine implementation prerequisites.
- Do not guess hardware-specific values.
- Do not claim real hardware verification unless actually performed.
- HARDWARE_MODE = mock | dry-run | real.
- CAMERA_MODE = mock | ustreamer.
- Same source tree on PC and Raspberry Pi.
- No emergency-stop UI control should be reintroduced.

Apply changes conservatively.

Recommended order:

1. AGENTS.md and skills
2. command files
3. BACKLOG.md
4. specs
5. docs

After changes, report:

1. Files modified.
2. Approximate lines removed.
3. Duplicated rules centralized.
4. Specs shortened.
5. Command-context optimizations.
6. Contradictions resolved.
7. Remaining open contradictions.
8. Confirmation that no source code was changed.
9. Confirmation that no task was started or implemented.
