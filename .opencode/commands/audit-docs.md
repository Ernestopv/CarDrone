Audit the Markdown documentation of this project for token usage, duplication,
contradictions, and unnecessary context loading.

Do not modify files.
Do not implement code.
Do not change task statuses.
Do not start any backlog task.

Review:

- AGENTS.md
- MEMORY.md if present
- README.md
- docs/**/*.md
- specs/**/*.md
- tasks/BACKLOG.md
- .opencode/commands/*.md
- .opencode/skills/*/SKILL.md

Goals:

- Find duplicated rules.
- Find very large specs.
- Find contradictions between backlog, specs, commands, and skills.
- Find rules that should live in AGENTS.md or a skill instead of being repeated.
- Find commands that load too much context.
- Find specs that unnecessarily repeat global Raspberry Pi, Docker, SDD, or validation rules.
- Identify places where NOT VERIFIED and BLOCKED semantics are inconsistent.

Preserve these project rules:

- One implementation task at a time.
- /plan-spec creates/updates specs only.
- /spec implements only the active spec.
- Do not automatically start the next task.
- NOT VERIFIED is not BLOCKED.
- Deferred real hardware validation does not block completion unless the active spec explicitly requires it.
- Do not guess Raspberry Pi device paths, GPIO APIs, permissions, wiring, PWM values, or motor behavior.
- Do not claim physical verification unless actually performed.
- HARDWARE_MODE = mock | dry-run | real.
- CAMERA_MODE = mock | ustreamer.
- Same source tree for PC and Raspberry Pi.

Return only an audit report containing:

1. Markdown files with the most duplication.
2. Specs that are unusually long.
3. Repeated rules that should be centralized.
4. Proposed authoritative file for each repeated rule.
5. Contradictions found.
6. Commands that could load less context.
7. Estimated safe changes.
8. Files that should NOT be touched.

Do not apply any changes.