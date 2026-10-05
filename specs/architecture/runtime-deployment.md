# Unified Runtime Architecture

## Purpose

Task 21 defines the final runtime architecture that lets the same project folder run on a Windows/Linux development PC and on a Raspberry Pi ARM64 with one command — `docker compose up` — with **zero source-code differences**. Platform behavior comes entirely from configuration/environment:

```text
Development PC                 Raspberry Pi ARM64
Frontend  REAL                 Frontend  REAL
Backend   REAL                 Backend   REAL
API       REAL                 API       REAL
Hardware  MOCK                 Hardware  REAL (or DRY-RUN)
Camera    MOCK                 Camera    REAL (uStreamer)
```

This is a **design task**: the deliverable is this architecture document plus durable docs updates. It writes no application code and no config files; every mechanism it fixes is implemented by later backlog tasks (mapping table below), which must not contradict it.

## Dependencies

- `specs/backend/drone-application-service.md` / `specs/backend/drone-simulator.md` — the existing `IDroneController` application contract and its `MockDroneController` implementation: the seam this architecture plugs hardware providers into.
- `specs/backend/drone-api.md` + `specs/backend/error-handling.md` — the frontend/backend HTTP contract (five endpoints + health, wire formats, 400/409/503/500) that **must be preserved unchanged** by this design.
- `specs/integration/frontend-backend.md` + `specs/integration/frontend-api-service.md` — the frontend `DroneService` abstraction, `ApiDroneService`, and the env-var selection seam (`VITE_DRONE_SERVICE`, `VITE_API_BASE_URL`) already proven on PC.
- `specs/docker/backend-container.md` + `specs/docker/multi-arch.md` (Tasks 13/15) — the ARM64-capable, architecture-neutral, non-root backend image: the proof that the *same image lineage* can run on PC and Pi.
- `specs/docker/full-stack-compose.md` (Task 20) — the one-command two-service stack and its same-origin nginx `/api/` proxy pattern, which the `/camera/` transport mirrors.
- `specs/frontend/camera.md` + `specs/frontend/error-states.md` — today's simulated camera states (the `mock` camera baseline) and UI error semantics.
- `AGENTS.md` — GPIO configuration values (Pin1=23, Pin2=24, Pin3=21, Pin4=20, PWM1=12, PWM2=13) treated **only as configuration**; layering rules (controllers never touch GPIO, Domain never depends on Infrastructure); hardware honesty rules.
- Backlog Tasks 22–24 (Phase 5), 25–30 (Phase 6 GPIO/motors), 31–33 (Phase 7 camera), 34+ (Phase 8 unified deployment) — the implementation owners boundaried below.

## Scope

This design fixes:

1. **Runtime mode vocabulary** — `HARDWARE_MODE = mock | dry-run | real` and `CAMERA_MODE = mock | ustreamer`, their exact meanings, defaults, and validation policy.
2. **Process topology** — which components run where on each platform, and the decision to *not* introduce a separate remote Raspberry Pi service (with the rejected alternative recorded).
3. **Layer seams** — where mode-based implementation selection happens (DI composition root), how `IDroneHardware` (Task 22) relates to the existing `IDroneController`, and what each layer is forbidden to know.
4. **Transport separation** — control plane (`/api/`, JSON, ASP.NET) vs media plane (`/camera/`, video, nginx→uStreamer), including the Pi-mode status reporting intent over the *unchanged* wire contract.
5. **Configuration mechanics** — how one compose file yields PC-mock vs Pi-real behavior using Compose v2 `.env` + profiles (identical literal command on both platforms).
6. **Failure and safety policy** — fail-fast rules for invalid/incompatible mode combinations, dry-run honesty, hardware verification-level vocabulary for downstream tasks.
7. **Prerequisites ledger** — every physical unknown that must be *verified or documented later* (wiring, PWM, devices, uStreamer), so no task silently invents it.
8. **Task mapping table** — which backlog task implements which element.

## Out of Scope

- Any application code, project file, Dockerfile, compose file, or configuration file change (hash-verified: none in this task).
- Defining `IDroneHardware`'s members/contract → **Task 22** (`specs/hardware/hardware-abstraction.md`).
- Raspberry GPIO/PWM provider implementation → **Task 23**; Docker device exposure/permissions → **Task 24** (explicitly forbids invented device paths).
- GPIO line/pin abstraction → **Task 25**; hardware fail-safe → **Task 26**; dry-run mode implementation → **Task 27**.
- L298N wiring documentation and the Pin↔IN1/IN2/ENA mapping → **Task 28**; direction semantics → **Task 29**; PWM frequencies/duty limits → **Task 30**.
- Camera runtime design details → **Task 31**; uStreamer container specifics → **Task 32**; frontend stream integration → **Task 33**; unified Pi deployment task → **Task 34+**.
- Physical hardware of any kind — this design runs on no Pi and verifies no electronics; all physical claims stay marked unverified.

## Architecture

### Process topology (the core decision)

```text
DEVELOPMENT PC (Windows/Linux)                     RASPBERRY PI ARM64
────────────────────────────                       ─────────────────────
docker compose up                                  docker compose up   ← identical command
├── cardrone-frontend (nginx)                      ├── cardrone-frontend (nginx)
│     ├── /        → built React app                     ├── /        → built React app
│     └── /api/    → proxy → backend                     ├── /api/    → proxy → backend
│         (no /camera/ proxy in mock mode)               ├── /camera/ → proxy → uStreamer*
└── cardrone-backend (aspnet, ARM-agnostic)        └── cardrone-backend (aspnet, arm64 build)
      Hardware: MockDroneController                     Hardware: Raspberry provider*
      Camera:   simulated (frontend state)              Camera:   /dev/video* via uStreamer*
      .env:     HARDWARE_MODE=mock, CAMERA_MODE=mock    .env:     HARDWARE_MODE=real|dry-run,
                                                                  CAMERA_MODE=ustreamer
      * = services/providers introduced by later tasks (22–24, 31–32)
```

**Decision D1 — No separate remote Raspberry Pi service.** The ASP.NET Core backend itself runs on the Pi and accesses hardware *locally* through abstractions; a PC-based backend talking over the network to a Pi-side agent is the rejected alternative. Rejection rationale: it duplicates contracts (two service layers to bridge), doubles the failure surface (network hop on the drone's own control path), contradicts "same source everywhere" less directly but operationally more expensively, and is not technically required — Tasks 13/15 already produced a portable ARM64 backend image, and the current single-container compose stack on PC becomes the identical stack on the Pi with different `.env`. Requirement source: backlog Task 21 "Avoid introducing a separate remote Raspberry Pi service unless technically required."

**Decision D2 — One compose file, `.env` + profiles for platform differences.** Compose v2 auto-loads `.env` from the project directory and honors `COMPOSE_PROFILES`; Pi-only additions (uStreamer service, device mounts) attach to profiles rather than to a second compose file or code branches. The literal command `docker compose up` is identical on both platforms; the folder's `.env` differs — which is precisely "platform differences from configuration/environment." Device-mount specifics stay with Task 24; this design reserves the seam. (Alternative considered: `docker-compose.override.yml` — rejected because overrides apply to *every* `up` regardless of platform, re-introducing the PC/Pi split outside config.)

### Layer seams

```text
React UI
  ↓ (no GPIO/Pi knowledge — unchanged)
DroneService abstraction (ApiDroneService ⇄ MockDroneService)   ← unchanged
  ↓ HTTP /api/…  (five endpoints + health)                       ← unchanged
ASP.NET Core API (thin controllers)
  ↓
Application layer: IDroneService → IDroneController               ← unchanged contract
  ↓ DI composition root selects implementation by HARDWARE_MODE:
Infrastructure layer
  ├── mock     → MockDroneController (exists today; simulated acks)
  ├── dry-run  → Raspberry provider stack with physical outputs logged,
  │              never applied (Task 27)
  └── real     → RaspberryDroneHardware behind IDroneHardware (Tasks 22/23)
                   ↓
                 GPIO / PWM abstractions (Tasks 25/26/29/30)
                   ↓
                 Linux GPIO character devices / PWM (Task 24; paths not invented here)
```

**Decision D3 — mode selection at the composition root only.** `Program.cs`-level registration (validated options → single `switch`) chooses which `IDroneController`/`IDroneHardware` wiring enters the graph. Below it, no `if (isPi)` checks: Application and Domain stay platform-agnostic (AGENTS: hardware-specific code isolated; Domain must not depend on Infrastructure — unchanged rule). The existing `IDroneController` remains the application's drone contract; `IDroneHardware` (Task 22) is the lower seam *consumed by* the real/dry-run controller implementations. Task 21 mandates the seam exists and is config-selected; Task 22 owns its exact members.

**Decision D4 — control plane vs media plane never merge.** Command/status traffic stays exactly as today on `/api/…`. Video is *streaming-only* over `/camera/` → uStreamer, proxied by nginx (mirroring the proven `/api/` pattern from Task 20), and uStreamer is never an ASP.NET concern (no video proxying through backend code). The frontend's camera *status* still arrives via the existing `DroneStatus.camera` field (wire unchanged); only the pixel stream is a separate transport. In `mock` camera mode nothing is served under `/camera/` and the current simulated `CameraView` remains (its URL wiring arrives in Task 33, which will consume this reserved path).

### Runtime mode semantics (normative)

| Key | Value | Meaning | Valid where |
| --- | --- | --- | --- |
| `HARDWARE_MODE` | `mock` (**default**) | Simulated drone side (`MockDroneController`); no hardware libraries loaded or attempted | Any platform |
| | `dry-run` | Raspberry hardware stack active; every physical operation is *logged and suppressed* — state may update, outputs must not actuate | Pi (or explicitly-allowed Pi-like env per Task 27) |
| | `real` | Physical GPIO/PWM applied | Pi only |
| `CAMERA_MODE` | `mock` (**default**) | No uStreamer service/profile; frontend camera stays simulated | Any platform |
| | `ustreamer` | uStreamer profile enabled; `/camera/` proxied; camera *status* reported through the existing `DroneStatus.camera` mapping (details per Task 31) | Pi |

Config validation policy (Decision D5): **strict, fail-fast, no silent fallback.** Unknown mode strings → startup refuses with a message naming the key and accepted values. `real` on a non-Pi (hardware preflight from Task 23 fails: no GPIO device, unsupported OS/arch) → startup fails clearly — never downgrades to mock (an operator must never believe the drone is live-simulating when they commanded `real`, or vice-versa). Defaults (`mock`/`mock`) mean a fresh clone + `docker compose up` reproduces exactly today's PC behavior: a property of this design, verified by zero code changes.

## Behavior

- **PC after Task 21 (and until Tasks 22–24 land):** unchanged — compose builds/starts the same two services as Task 20; hardware is the existing mock; camera stays simulated.
- **Pi (once Tasks 22–24, 31–32 are implemented):** same source, same compose files; the Pi folder's `.env` selects `real`/`ustreamer`; `docker compose up` yields nginx on Pi (browser access via the Pi's LAN address on the already-defined host ports), backend with the Raspberry provider, uStreamer under its profile. Repeated `up`/`down` must not require any rebuild of application code.
- **Status reporting in Pi mode (intent; contracts owned by later tasks):** `DroneStatus.raspberryPi` transitions from simulated values to provider-reported availability; `DroneStatus.camera` reflects the real stream pipeline's health as defined in Task 31. The JSON shape the frontend receives does not change — preservation requirement from the backlog.
- **Acknowledgement honesty across modes:** `mock` acks remain labeled simulated (today's wire truth); `dry-run`/`real` ack semantics and the hardware-source labeling problem are explicitly delegated to Tasks 26/27/31+ — this design only forbids *any* mode from claiming hardware confirmation without the real-hardware verification level being reached (verification ladder below).

## Interfaces

Configuration contract established by this design (binding mechanism is finalized by Task 22; the *keys, values and defaults* are fixed here):

| Key | Accepted values | Default | Source |
| --- | --- | --- | --- |
| `HARDWARE_MODE` | `mock`, `dry-run`, `real` | `mock` | env var (compose `.env`), overridable per standard ASP.NET configuration hierarchy |
| `CAMERA_MODE` | `mock`, `ustreamer` | `mock` | env var (compose `.env`) |
| Compose profiles | `camera` (uStreamer + `/camera/` location enabling), names fixed by Tasks 31/32; hardware device mounts are Task 24's | none enabled | `COMPOSE_PROFILES` in `.env` |
| GPIO config | Existing `GPIO` section (Pin1=23, Pin2=24, Pin3=21, Pin4=20, PWM1=12, PWM2=13) — read as configuration; **semantic role assignment happens only in Tasks 25/28/29/30** | — | appsettings/env per existing config conventions |

Reserved network paths: `/api/…` (control plane, exists), `/camera/…` (media plane, reserved — inactive while `CAMERA_MODE=mock`).

## Task Mapping

Every mechanism reserved by this design, with the backlog task that owns it (no mechanism is implemented by Task 21 itself):

| Design element (this document) | Owner | Task spec path |
| --- | --- | --- |
| `IDroneHardware` abstraction + `MockDroneHardware`/`RaspberryDroneHardware` seam below `IDroneController` (D3) | Task 22 | `specs/hardware/hardware-abstraction.md` |
| Raspberry hardware provider + platform preflight that makes `real` fail clearly on unsupported platforms (D5) | Task 23 | `specs/hardware/raspberry-hardware-provider.md` |
| Linux GPIO/PWM device paths, permissions, compose device exposure, minimal-privilege proof | Task 24 | `specs/docker/raspberry-hardware-access.md` |
| GPIO line abstraction and config binding of the known pin values | Task 25 | `specs/hardware/gpio.md` |
| Hardware fail-safe (including any `real`-mode loss-of-command behavior) | Task 26 | `specs/hardware/failsafe.md` |
| Dry-run implementation (log-and-suppress wiring, per the semantics fixed in this design) | Task 27 | `specs/hardware/dry-run.md` |
| L298N wiring documentation — the authoritative source for pin↔IN/EN semantics | Task 28 | `specs/hardware/motor-wiring.md` |
| Motor direction mapping (only once Task 28 confirms wiring) | Task 29 | `specs/hardware/motor-control.md` |
| PWM frequency / duty ranges and electrical limits | Task 30 | `specs/hardware/pwm.md` |
| Camera runtime + mapping of real camera status onto `DroneStatus.camera` | Task 31 | `specs/hardware/camera-runtime.md` |
| uStreamer container (service definition, profile name, ports, endpoints) | Task 32 | `specs/docker/ustreamer-container.md` |
| Frontend `/camera/` stream integration (consumes this design's reserved path) | Task 33 | `specs/integration/camera-stream.md` |
| Unified compose deployment mechanics (final `.env`/profiles form; one command on both platforms) | Task 34 | `specs/docker/unified-compose.md` |
| PC runtime mode verification | Task 35 | `specs/deployment/pc-mode.md` |
| Raspberry Pi runtime mode verification | Task 36 | `specs/deployment/raspberry-mode.md` |
| End-to-end control / camera / failure-recovery validation | Tasks 37–39 | `specs/integration/end-to-end-control.md`, `specs/integration/end-to-end-camera.md`, `specs/integration/failure-recovery.md` |
| MVP final validation | Task 40 | `specs/deployment/mvp-final-validation.md` |

## Prerequisites Ledger (unverified physical facts — never invented here)

Each row is a fact this design **deliberately does not answer**; it must be verified or documented by the owning task before any dependent behavior can claim a verification level above `design only`.

| Unverified fact | Forbidden invention | Owning task(s) |
| --- | --- | --- |
| Raspberry Pi model / OS release / kernel for the target device | any specific board or OS claim | 23, 24, 34, 36 |
| GPIO character-device path(s) on the Pi (e.g. which `/dev/gpiochipN`, if any) | device paths or access APIs | 24; concrete real sink implemented on the char-device (ioctl v1 UAPI, `RaspberryGpioPlatform`); chip path config-driven (`Raspberry:GPIO:ChipPath`, default the inventory-detected `/dev/gpiochip0`); non-root container access still pending a target permission decision |
| PWM interface (which subsystem drives GPIO12/GPIO13, API, clock source) | sysfs paths, driver choices | 24, 30; concrete sysfs PWM real sink implemented (`RaspberryPwmPlatform`); chip path + identifier→channel mapping are operator-supplied from target evidence |
| Linux user/group permissions required for GPIO/PWM (and whether they can be granted without privileged mode) | `privileged: true` by default | 24 |
| Electrical semantics of the known GPIO config (which of 23/24/21/20 drives which L298N input; whether 12/13 are ENA/ENB) | pin↔IN/EN tables, HIGH/LOW combinations, motor direction truth | 28 → then 29/30 |
| L298N power topology (VM/Vs, common ground, current limits, enable wiring) | any electrical claim | 28 |
| PWM frequency and safe duty-cycle envelope for the motors | frequency/range values | 30 |
| Fail-safe expectations beyond STOP (signal loss, watchdogs, power loss behavior) | recovery semantics | 26 |
| Camera device presence/path and uStreamer build, version, port, MJPEG endpoint layout | `/dev/video*` paths, URLs, stream URLs | 31; 32 (uStreamer facts determined: tag `v6.67`, port `8080`, endpoint `/stream`); 24/36 (camera device path) |
| How the frontend consumes the stream (`<img>` MJPEG vs other) | stream client mechanics | 33 |
| Acknowledgement source on the wire: today's `POST /api/drone/command` response carries **no** field distinguishing simulated vs hardware confirmation, and the frontend adapter hardcodes `source: 'simulated'` | any `hardware` acknowledgement claim in any mode | 26/27 + whichever task first needs real confirmation — must extend the wire contract (and preserve the existing shape for simulated acks) |
| Actual `linux/arm64` container runtime behavior on physical Pi hardware (builds only proven in Task 15) | Pi runtime claims | 34, 36 |

## Validation

Design-level checks performed in the implementation turn:

1. Document consistency: every decision here re-checked against the referenced specs/AGENTS rules (wire preservation, layer isolation, no invented hardware).
2. Contradiction audit against backlog Tasks 22–34 requirements (each task's listed requirements must remain satisfiable without violating this design).
3. Zero-drift proof: file hashes before/after (no source, no compose, no Dockerfile, no config files changed) — this task must not even "prepare" config values for later tasks.
4. Prerequisites ledger completeness: every mechanism that depends on unverified physical facts is listed as a prerequisite with its owning task; nothing is answered with guessed values.

## Error Cases (policy level; per-component behavior owned by later tasks)

- Invalid/unknown `HARDWARE_MODE` or `CAMERA_MODE` value → startup failure with explicit key/value error (validated options), never defaults.
- `HARDWARE_MODE=real` (or `dry-run`) on a platform without working hardware access → fail fast at provider preflight (Task 23 owns detection; behavior fixed here: abort startup, log reason, no mock fallback).
- uStreamer profile enabled (`ustreamer`) but stream service unavailable → drone control must remain functional (control plane decoupled by D4); camera status surfaces via the existing field (Task 31 defines the mapping); `/camera/` requests may fail without affecting `/api/`.
- Hardware operation failures in `real` mode (provider errors) → surface through existing Application-layer error contracts (`DroneUnavailableException` channel from Task 16, 503 wire mapping) — this design forbids inventing new silent-recovery paths; fail-safe behavior belongs to Task 26.
- Frontend in Pi-real mode never learns pin/config values — enforced by wire contract (no pin fields exist), not by convention alone.

## Platform Requirements

- PC: existing Docker Desktop Linux-engine workflow (Tasks 14/20) — unchanged, defaults give mock/mock.
- Pi: `linux/arm64` runtime for all containers: backend already validated as multi-arch-neutral (Task 15); nginx/node base images used here are multi-arch by official distribution (verify per image in Task 34's deployment validation).
- One-command startup on both platforms: `docker compose up` (or `-d`), never requiring per-platform commands, flags beyond `.env`, or code.
- Raspberry hardware, devices, uStreamer, GPIO permissions: **not present and not exercised in this design phase** — all Pi-side execution requirements are consolidated as prerequisites in the ledger below for Tasks 24/31/32/34.

## Security / Safety

- **No invented physical behavior.** The GPIO numbers are configuration only; HIGH/LOW combinations, IN1–IN4/ENA/ENB assignments, PWM frequency/duty safety ranges, device paths, and camera device specifics remain open until their documenting tasks (24/25/28/29/30/31). This design assigns zero electrical meaning to any pin.
- **Fail-fast over fallback** (D5): an operator's declared intent (`real`) must never be silently downgraded to `mock`; conversely nothing `mock`-mode ever does may claim hardware truth (existing rule, unchanged).
- **Dry-run honesty:** `dry-run` exists to exercise the Pi runtime path while physically inert — logs, never motion. Implementation contract is Task 27's; the semantics are fixed here.
- **Verification vocabulary (normative for all later hardware tasks):** every downstream acceptance must state its level — `design only` / `implemented` / `built` / `mock-tested` / `dry-run-tested` / `Pi-runtime-verified` / `real-hardware-verified`. Higher levels may never be implied by lower-level evidence; this design's own level is `design only`.
- Containers stay non-root (backend image, Task 13) and unprivileged; privileged containers are a last resort requiring proof (Task 24's mandate).
- The drone can always be commanded STOP over `/api/` in every mode (control-preserved invariant; hardware-level fail-safe is Task 26).

## Testing Scenarios

This task is design-only; implementation turns execute:

1. Write/finalize this document (planning produces it; implementation reviews it line-by-line against Dependencies).
2. Run the Validation section checks (consistency, contradiction audit against Tasks 22–34, prerequisites ledger).
3. Create/refresh the durable architecture records: `docs/ARCHITECTURE.md` (current snapshot of layers, runtimes, transports, modes — as it exists today *plus* this design's reserved seams, each marked with its status: `implemented today` vs `design for Tasks 22+`) and `docs/DECISIONS.md` (D1–D5 + the mock/dry-run/real vocabulary, each with context/decision/consequences/revisit-when).
4. Zero-drift hash audit (nothing outside docs/specs/MEMORY/backlog changed) + build/test regression (`dotnet build`, `dotnet test`, `npm test` unchanged — they must still pass because no code was touched).
5. Spot-check the design's PC-mode claim: `docker compose config` with an unset `.env` still resolves to the two existing services with no profile-gated extras (asserts defaults are truly today's behavior).

## Acceptance Criteria

- [x] `specs/architecture/runtime-deployment.md` (this file) exists and no application source, Dockerfile, compose file, or configuration file was modified by the task (hash audit + `dotnet build` 0/0, `dotnet test` 51/51, `npm test` 121/121 all unchanged).
- [x] The design defines `HARDWARE_MODE=mock|dry-run|real` and `CAMERA_MODE=mock|ustreamer` with exact semantics, defaults (`mock`/`mock`), strict validation (unknown values → startup failure; no silent fallback), and the fail-fast rule for `real`/`dry-run` on unsupported platforms.
- [x] Decision D1 is recorded: the backend runs on the Pi accessing hardware locally through abstractions; the remote-Pi-service alternative is listed with explicit rejection rationale, satisfying the backlog's "avoid separate remote Pi service" requirement.
- [x] Decision D2 is recorded: one `docker compose up` command on both platforms; platform differences limited to `.env`-delivered configuration and compose profiles; the override-file alternative is considered and rejected with reason.
- [x] Decision D3 is recorded: mode-based implementation selection happens only at the DI composition root; `IDroneController` stays the application contract; `IDroneHardware` is reserved as Task 22's seam *consumed below* that contract; Domain/Application keep zero hardware dependencies (consistent with existing layering rules).
- [x] Decision D4 is recorded: `/api/` control-plane contract (five endpoints + health, wire formats, ProblemDetails statuses) is preserved byte-for-byte; `/camera/` is reserved for the media plane via nginx; uStreamer never passes through ASP.NET; mock mode serves no `/camera/` traffic and keeps today's simulated CameraView.
- [x] The PC-vs-Pi matrix matches the backlog's mode tables exactly (Frontend/Backend/API real on both; Hardware/Camera mock on PC, real on Pi) and the document states which task makes each Pi row true.
- [x] A task-mapping table assigns every reserved mechanism to its owning task (22 `IDroneHardware`, 23 provider, 24 device access, 25 GPIO, 26 fail-safe, 27 dry-run, 28 wiring, 29 direction, 30 PWM, 31 camera runtime, 32 uStreamer container, 33 frontend stream, 34+ unified deployment) with no mechanism implemented here.
- [x] The prerequisites ledger lists every unverified physical fact (pin↔L298N semantics, PWM frequency/duty limits, GPIO device paths/permissions, uStreamer endpoints/config, camera device presence, Pi model/OS assumptions, ARM64 compose runtime behavior) tagged with its owning task; none carries an invented value.
- [x] The verification-level vocabulary (design only / implemented / built / mock-tested / dry-run-tested / Pi-runtime-verified / real-hardware-verified) is defined and required for all later hardware acceptance criteria; this task's own level is stated as design only.
- [x] `docs/ARCHITECTURE.md` and `docs/DECISIONS.md` are created during implementation, capturing the current architecture snapshot (with implemented-vs-designed statuses per element) and decisions D1–D5 in ADR style.
- [x] Backlog Task 21 moves to COMPLETED only after the design-level Validation section checks pass, and no code artifact appears in the diff beyond documentation files.
