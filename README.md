# CarDrone — Drone Control

A web application for **monitoring and controlling a small wheeled drone**: a
React dashboard, an ASP.NET Core backend, and a Raspberry Pi hardware runtime
(GPIO/PWM for the motors, a USB camera streamed with uStreamer).

The same source tree runs on a **development PC** (simulated hardware) and on a
**Raspberry Pi** (real hardware). Platform differences come only from
configuration — no code branches.

> Rulebook: `AGENTS.md`. Current architecture snapshot: `docs/ARCHITECTURE.md`.
> Durable decisions: `docs/DECISIONS.md`. Task roadmap: `tasks/BACKLOG.md`.

---

## 1. What it does

- **Dashboard**: connection/system status, simulated telemetry, a **camera
  panel** (live MJPEG feed on the Pi, simulated on the PC), and directional
  controls (forward / backward / left / right / stop) plus a speed control.
- **Real control plane** over `/api/…` (five endpoints + health): connect,
  disconnect, command, speed, status.
- **Two runtime planes that never mix** (D4): the control plane (`/api/`, JSON,
  ASP.NET) and the media plane (`/camera/`, MJPEG, nginx → uStreamer).
- **Honest by construction**: simulated acknowledgements are never presented as
  hardware-confirmed; anything not physically verified is marked
  `NOT VERIFIED`.

## 2. Architecture

```text
Browser (React SPA)
   │  same-origin HTTP
   ▼
Frontend nginx ── /        → static React build
   ├──────────── /api/     → ASP.NET Core backend
   └──────────── /camera/  → uStreamer (MJPEG)          [media plane]
                                ▲
                                │ /dev/video0
                          Raspberry Pi Camera
```

Layers (strict boundaries — hardware knowledge stays low):

```text
React UI                presentation only; knows no GPIO/PWM/device details
   ↓  DroneService      frontend service abstraction (ApiDroneService | MockDroneService)
ASP.NET Core API        thin controllers, wire contract (camelCase, lowercase enums, ProblemDetails)
   ↓
Application             IDroneService → IDroneController (contract); DroneService orchestration
   ↓  composition root  selects ONE graph by HARDWARE_MODE (mock | dry-run | real)
Infrastructure          MockDroneController | Raspberry provider (IDroneHardware)
   ↓
Linux / Raspberry Pi    GPIO (libgpiod), PWM (sysfs), camera device
```

- **Domain** (`DroneControl.Domain`): immutable records/enums. Depends on nothing.
- **Application**: contracts + orchestration; never references hardware.
- **Infrastructure**: hardware-boundary implementations and the fail-safe
  decorator.
- **API**: HTTP surface only; no logic; controllers never touch GPIO.

### Camera pipeline

```text
USB camera → uStreamer (MJPEG) → nginx /camera/ → browser <img>
                     ▲
   backend probes /camera/ (headers-only) → DroneStatus.camera (reachable = "streaming")
```

The browser always uses the **relative same-origin `/camera/`** path (no host/IP
in React). The backend reports camera *status* over the existing
`DroneStatus.camera` field — the wire contract is unchanged.

## 3. Runtime modes and deployment

Two orthogonal runtime modes, selected **only at DI composition root**:

| Key | Values | Meaning |
| --- | --- | --- |
| `HARDWARE_MODE` | `mock` (default) \| `dry-run` \| `real` | mock = simulated; dry-run = real flow, outputs logged & suppressed; real = physical GPIO/PWM |
| `CAMERA_MODE` | `mock` (default) \| `ustreamer` | mock = simulated camera; ustreamer = real MJPEG via `/camera/` |

One folder, one command: **`docker compose up`** on PC and Pi; the only
difference is the folder's `.env`. Invalid/incompatible values abort startup
loudly (no silent fallback, D5). `real` requires `linux/arm64` and, for motor
actuation, the external-failure-protection assertion (D7).

See `docs/DECISIONS.md` (D1–D7) and `docs/ARCHITECTURE.md`.

## 4. Repository layout

```text
backend/     .NET 10 solution: Domain, Application, Infrastructure, Api (+ tests)
frontend/    React + TypeScript + Vite dashboard (nginx image, /api + /camera proxies)
camera/      uStreamer container image (MJPEG encoder, profile "camera")
docker-compose.yml             base stack (frontend, backend, ustreamer[profile camera])
docker-compose.raspberry.yml   Pi-only overlay (device/group mappings)
.env.raspberry.example         Pi configuration template
docs/        PRD, ARCHITECTURE, DECISIONS, DOCKER, RUNBOOK-PI, hardware/WIRING.md, ...
specs/       one specification per feature/task (the SDD source of truth)
scripts/     native (no-Docker) full-stack launcher for the Pi
tasks/BACKLOG.md               roadmap and task statuses
```

## 5. Running it

### Development PC (Docker)

```bash
docker compose up
# UI:  http://localhost:8081      API: http://localhost:5080
```

### Raspberry Pi (Docker)

```bash
cp .env.raspberry.example .env      # HARDWARE_MODE=real, CAMERA_MODE=ustreamer, GPIO_*, ...
docker compose up --build
```

Device/permission mappings live in `docker-compose.raspberry.yml` and must come
from the completed target inventory — nothing is invented. Step-by-step:
`docs/RUNBOOK-PI.md`.

### Raspberry Pi (native, no Docker)

Useful when the process must write `/sys/class/pwm` (Docker mounts `/sys`
read-only):

```bash
sudo apt-get install -y nginx ustreamer libgpiod2
chmod +x scripts/run-native-pi.sh
cp scripts/native.env.example native.env
sudo ./scripts/run-native-pi.sh        # uStreamer + backend + nginx; --stop to stop
```

## 6. Hardware

- Raspberry Pi 4 (a **Raspberry Pi 4 Model B, Ubuntu 22.04 arm64** on the current
  target), L298N motor driver, two DC motors, a USB camera.
- GPIO character device `/dev/gpiochip0` (BCM2711 header bank). The non-root
  container receives the `gpio` group; no `privileged` mode.
- Physical wiring, PWM limits and motor behavior are recorded in
  `docs/hardware/WIRING.md` and are **operator evidence** — never assumed.
- Verification ladder (normative): `implemented → built → mock-tested →
  dry-run-tested → Pi-runtime-verified → real-hardware-verified`. Anything not
  physically exercised stays `NOT VERIFIED`.

## 7. Development workflow (Spec-Driven Development)

1. **`/plan-spec <task>`** creates or updates a specification only (no code).
2. **`/spec <spec>`** implements only the active specification and validates it.
3. One task at a time; the next task is never started automatically.

The specification is the source of truth. `tasks/BACKLOG.md` holds the roadmap
and statuses; each task's spec lives under `specs/`. Work proceeds in phases
(frontend → backend → hardware → camera → unified deployment → end-to-end
validation).

## 8. Status

- Implemented and mock/dry-run-tested: dashboard, backend API + simulator,
  fail-safe software supervision, dry-run mode, motor direction + PWM mapping
  (software), real GPIO sink (libgpiod), camera probe + status overlay,
  `/camera/` proxy, uStreamer container, unified compose + Pi `.env`.
- Verified on the target Pi: stack runs, camera MJPEG pipeline, GPIO container
  access (non-root), motor direction over libgpiod.
- `NOT VERIFIED` (deferred / needs hardware evidence): real motor actuation
  end-to-end, variable speed via PWM (sysfs routing + permissions), full Pi
  runtime sign-off. See `docs/hardware/raspberry-pi-inventory.md`.

## 9. Development tooling (OpenCode + skills)

CarDrone is built with **[OpenCode](https://opencode.ai)**, an agentic coding
harness, driven by project-local **skills** and slash commands rather than
ad-hoc prompting. Code is produced through the Spec-Driven Development workflow
(§7).

Skills actually used on this project (kept under `.agents/skills/` and
`.opencode/commands/skills/`):

| Skill | Used for |
| --- | --- |
| `spec-driven-development` | the workflow itself — `/plan-spec` → `/spec` → validate; backlog lifecycle; `NOT VERIFIED` / `BLOCKED` honesty rules |
| `dotnet` | C# / ASP.NET Core conventions — layering, DI composition, configuration, xUnit tests |
| `docker` | multi-stage images, `.dockerignore`, Compose, arm64 builds, orchestrator-level health probes |
| `raspberry-pi` | Pi runtime — GPIO/PWM, camera/uStreamer, Linux device access, hardware permissions |
| `react` | React + TypeScript UI patterns and component structure |
| `frontend-design` | visual design direction for the dashboard |
| `web-design-guidelines` | UI/accessibility review of the dashboard |

### Commands (`.opencode/commands/`)

The day-to-day workflow is driven by project commands kept in
**`.opencode/commands/`** (the `skills/` subfolder holds the prompt-side skill
descriptions):

| Command | File | What it does |
| --- | --- | --- |
| `/plan-spec` | [`plan-spec.md`](.opencode/commands/plan-spec.md) | create or update a **specification only** — no code |
| `/spec` | [`spec.md`](.opencode/commands/spec.md) | implement exactly **one** active specification and validate it |
| `/audit-docs` | [`audit-docs.md`](.opencode/commands/audit-docs.md) | **audit** documentation against the backlog, specs, commands and skills; flag contradictions and rules living in the wrong place |
| `/optimize-docs` | [`optimize-docs.md`](.opencode/commands/optimize-docs.md) | **optimize** the specs and docs — consolidate, remove duplication, and realign with the skills |

`/plan-spec` and `/spec` drive implementation; `/audit-docs` and
`/optimize-docs` were used to audit and optimize the specification set and the
documentation, keeping them consistent and lean.

The agent selects the **minimum set** of skills that the task's domain requires
— skills define *how*, the specification defines *what*.

## 10. Documentation map

- `AGENTS.md` — project rules, boundaries, workflow (start here)
- `MEMORY.md` — durable state and lessons learned
- `docs/PRD.md` — product requirements
- `docs/ARCHITECTURE.md` — architecture and status snapshot
- `docs/DECISIONS.md` — decision records D1–D7
- `docs/DOCKER.md` — container build/run commands
- `docs/RUNBOOK-PI.md` — bring-up runbook for the Raspberry Pi
- `docs/hardware/WIRING.md` / `raspberry-pi-inventory.md` — hardware evidence
- `scripts/run-native-pi.sh` — native (no-Docker) full-stack launcher
- `tasks/BACKLOG.md` — roadmap and task statuses
- `specs/` — one specification per feature/task
