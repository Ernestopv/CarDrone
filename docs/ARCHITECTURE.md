# Architecture — CarDrone

Durable snapshot of the system as it exists today, plus the seams reserved by
`specs/architecture/runtime-deployment.md` (Task 21, design-only).

Status legend:

```text
[implemented]  built and validated in the current codebase
[design]       fixed by an approved spec, owned by a later task — NOT yet code
```

## System overview

```text
Browser
  ↓ same-origin HTTP
nginx — frontend container                                   [implemented — Tasks 18–20]
  ├── /        static React build (dist)
  ├── /api/    proxy → http://backend:8080 (compose DNS)
  └── /camera/ MJPEG passthrough → uStreamer          [implemented/mock-tested — Tasks 32–33; Pi runtime & real video NOT VERIFIED]
       ↓
ASP.NET Core backend                                         [implemented — Tasks 8–12, 16–17]
  ├── API layer        thin controllers, wire contract (camelCase, lowercase
  │                    string enums, ProblemDetails 400/409/503/500)
  ├── Application      IDroneService → IDroneController + exception contracts
  └── Infrastructure   MockDroneController (simulated singleton)
      IDroneHardware seam + MockDroneHardware                [implemented — Task 22 (mock-tested)]
      Mode selection + Raspberry provider (inert) + bridge   [implemented — Task 23 (mock-tested; arm64 BUILD-verified, Pi runtime NOT verified)]
      GPIO config + mock + platform-port adapter              [implemented/mock-tested — Task 25]
      Pi Compose overlay boundary                              [implemented — Task 24; mappings deferred]
      Concrete Linux GPIO / PWM real sinks                 [implemented — GPIO char-device ioctl + sysfs PWM; Pi chip/permission evidence NOT VERIFIED]
      Fail-safe software coordinator                         [implemented/mock-tested — Task 26; real mode gated]
      Dry-run output boundary (record + suppress)            [implemented/mock-tested — Task 27]
      Wiring documentation (WIRING.md; fields NOT VERIFIED)  [implemented — Task 28; physical evidence deferred]
      Motor direction mapping (MotorController + assertion)  [implemented/mock-tested — Task 29; inactive by default, no mapping asserted]
      PWM speed mapping (SpeedController + assertion)        [implemented/dry-run-tested — Task 30; real sysfs sink implemented; Pi chip/channel evidence NOT VERIFIED]
      Camera runtime (CAMERA_MODE + probe status mapping)   [implemented/mock-tested — Task 31; ustreamer needs Pi runtime]
      uStreamer container (profile camera; no host port)    [implemented/mock-tested — Task 32; device mapping & Pi runtime NOT VERIFIED]
      Battery runtime (BATTERY_MODE + INA219 I2C read; current/power) [implemented; Pi Docker read VERIFIED — Tasks 41/42, 2026-10-06]
```

## Layers and rules

- **Domain** (`DroneControl.Domain`): pure records/enums (`DroneState`,
  `DroneStatus`, `BatteryStatus`/`BatteryState`, speed 0–100
  reject-don't-clamp). Depends on nothing.
- **Application**: `IDroneService`/`IDroneController` contracts, `DroneService`
  orchestration, `DroneNotConnectedException` + `DroneUnavailableException`
  (the latter reserved for the hardware phase), and the read-only
  `IBatteryMonitor` (Task 41). Never references hardware.
- **Infrastructure**: hardware-boundary implementations. Today: the simulator,
  the mock GPIO controller, and `RaspberryGpioController` delegating through
  `IRaspberryGpioPlatform`. A concrete Linux GPIO API/device adapter is deferred
  until target runtime integration; GPIO details stay invisible above this layer.
- **API**: HTTP surface only — five drone endpoints + `/api/health` +
  `/api/battery` (Task 41, read-only); input validation at the boundary,
  ProblemDetails error contract, no logic.
- **Frontend**: React + TypeScript + Vite. UI imports the `DroneService`
  abstraction; `ApiDroneService` (real HTTP, wire→UI mapping) or
  `MockDroneService` selected by `VITE_DRONE_SERVICE` (default `api`);
  `fetch` exists only inside `ApiDroneService`. Battery telemetry polls
  `getBattery()` into a `BatteryPanel` (Task 41; simulated reading is labelled).
  Acknowledgements are honest by
  construction: `source: 'simulated'`, never hardware (wire carries no source
  field — extension owned by the hardware-phase tasks).
- **Hard rule everywhere**: no GPIO/pin/PWM/device knowledge above
  Infrastructure, and none in the frontend, in any mode.

## Runtime configuration model

```text
HARDWARE_MODE = mock (default) | dry-run | real
CAMERA_MODE   = mock (default) | ustreamer
SAFETY.ExternalAbruptFailureProtectionVerified = false (default)
# MotorMapping.DirectionMappingVerified = false (default; no MotorMapping section ships)
PwmMapping.SpeedMappingVerified = false (default; no PwmMapping section ships)
BATTERY_MODE = mock (default) | ina219
```

Selected at the DI composition root only; strict validation, fail-fast —
`real` or `dry-run` on an unsupported platform aborts startup and neither ever
silently falls back to mock. Status: `HARDWARE_MODE` selection is
**implemented** (Task 23: `mock` default, `real` behind an OS/arch gate) and
`dry-run` is **implemented** (Task 27: same hardware flow as `real`, intended
GPIO operations recorded and suppressed at an Infrastructure output sink that
the graph cannot replace with a real one, GPIO configuration validated at
startup); `CAMERA_MODE` is **implemented** (Task 31: `mock` default — zero
camera registrations, the `Camera:` section never read; `ustreamer` behind the
same linux/arm64 gate plus a required absolute `Camera:ProbeUrl`, feeding
`DroneStatus.camera` from a headers-only reachability probe composed below the
safety decorator — `streaming` means endpoint reachability, never verified
video), and the `/camera/` transport is **implemented** (Task 32: nginx
`/camera/` location — empty target `404` never the SPA fallback, set target
byte passthrough — plus the `ustreamer` MJPEG service under the `camera`
compose profile; mock-tested on the PC, with camera device mapping, Pi
runtime, and real video `NOT VERIFIED`); the deployment wiring is **implemented** (Task 34:
`.env.raspberry.example` carries `CAMERA_MODE=ustreamer`,
`COMPOSE_PROFILES=camera`, `CAMERA_PROBE_URL=http://frontend/camera/`, and
`CAMERA_PROXY_TARGET=http://ustreamer:8080/stream`; the PC needs no `.env`),
and the React consumer is **implemented** (Task 33: the
browser reads a same-origin capability document `/camera-mode.json` for the
deployment mode, renders the MJPEG `<img>` with honest states and bounded-
backoff reconnect, and never shows simulated video in real mode; mock-tested,
with end-to-end real video `NOT VERIFIED` — Task 38). The provider stays inert — motor wiring remains unverified
until Task 28. Semantics, decisions D1–D5
and the prerequisites ledger live in `specs/architecture/runtime-deployment.md`
and `docs/DECISIONS.md`.

Task 41 adds battery monitoring (`specs/hardware/battery-monitoring.md`):
`BATTERY_MODE=mock|ina219` selected at the composition root. `mock` returns a
deterministic simulated reading (`simulated:true`, labelled in the UI);
`ina219` passes the linux/arm64 gate plus a required, validated `Battery:`
configuration (I2C device/address, voltage window) and registers the read-only
INA219 I2C monitor behind `GET /api/battery` (measured bus voltage + linear
state-of-charge approximation; a failed read returns an honest
`available:false`/`state:error` status with HTTP 200 — never a 5xx). The
frontend polls `getBattery()` into a `BatteryPanel`. Task 42 (`specs/hardware/battery-current.md`)
extends the reading with signed `current`/`power` computed from the shunt
register and the operator `ShuntOhms` (sensor stays read-only). Battery is an
independent concern from `HARDWARE_MODE`. Software
builds/tests verified; **Pi Docker runtime verified** (2026-10-06): INA219 at
`0x42`, non-root container reads `/dev/i2c-1` (gid 119), `GET /api/battery`
returned ≈6.17 V / −1.1 A / −6.5 W (≈7 % critical); the ±3.2 A shunt
saturation is a recorded limitation, and the **sign convention is
`CONFIRMED`** by operator bench observation (negative = charging, positive =
discharging).

PC runtime mode is verified end-to-end on a development PC (Task 35,
`specs/deployment/pc-mode.md`): a fresh copy of the project with no `.env`
starts with `docker compose up`, the control plane is real (connect/command/
speed/error contract over the wire), hardware and camera are mock
(`/camera/` 404, `camera-mode.json {"mode":"mock"}`), and the stack is
device-free. Raspberry Pi runtime remains `NOT VERIFIED` (Task 36).

Task 26 adds bounded software supervision and graceful startup/shutdown STOP
requests. `HARDWARE_MODE=real` remains faulted unless the external abrupt-failure
protection flag is explicitly enabled after independent verification; that flag
does not itself prove a physical safety mechanism. Physical STOP remains
unverified.

Task 27 adds `HARDWARE_MODE=dry-run`: the same hardware-backed controller flow
as `real`, with every intended GPIO/PWM operation recorded
(`dryRun=true`, `physicallyApplied=false`) and suppressed at the lowest
Infrastructure output sink. The real sink types are never registered in a
dry-run graph, so physical output is structurally unreachable — not toggled by
a runtime flag. Dry-run is not D7-gated (nothing in the graph can energize),
and it does not prove physical STOP or motor behavior.

Task 29 adds the motor direction mapping layer: an Infrastructure
`MotorController` applying a validated, OPERATOR-ASSERTED `MotorMapping`
(five commands, full `Pin1`–`Pin4` level coverage including an explicit
`stop` entry) through `IGpioController`, consumed only by the Raspberry
provider. Activation matrix at the composition root: no `MotorMapping`
section (the shipped default) or a valid-but-unasserted mapping keeps the
provider exactly inert (Unavailable/503, zero output operations); a valid
mapping with `DirectionMappingVerified=true` activates it in `dry-run`
(same suppressing sink; `real` aborts — no real sink exists until the
deferred Tasks 24/25); any invalid mapping content aborts startup with the
full error list. The flag is an operator assertion, not proof — the
procedure requires the `WIRING.md` direction record first (all rows
currently `NOT VERIFIED`). Speed is a separate capability (Task 30, below),
and `real` stays double-gated (mapping assertions + D7, all default `false`).

Task 30 adds the PWM speed mapping layer: an Infrastructure
`SpeedController` applying a validated, OPERATOR-ASSERTED `PwmMapping`
(frequency and duty bounds supplied by the operator; speed `0` → duty `0`,
speed `1..100` → `Min + (Max − Min) × speed / 100`, integer truncation)
through a new `IPwmController` seam — `DryRunPwmController` (record +
suppress, category `Pwm`) in `dry-run`, and a `RaspberryPwmController`
adapter over a deferred, library-free `IRaspberryPwmPlatform` port (the
concrete Linux PWM subsystem remains an open prerequisite; nothing is
invented). The activation matrix mirrors Task 29: no section (the shipped
default) or a valid-but-unasserted mapping registers nothing PWM-related and
issues zero operations; asserted + `dry-run` activates it (four suppressed
records per speed application); asserted + `real` aborts startup naming
`IRaspberryPwmPlatform`; invalid content aborts with the aggregated error
list. The provider becomes capability-aware: direction and speed report
independently, a missing capability gets a specific 503 message naming its
required section/flag, and full inertness keeps the established default
message. Requested/applied speed tracking
(`LastRequestedSpeedPercent`/`LastAppliedDutyPercent`) is
Infrastructure-internal — no wire, Domain, or frontend field. Graceful
disposal issues duty `0` only when a non-zero duty was applied; failures
propagate. The flag is an operator assertion, not proof — the procedure
requires the `WIRING.md` `PWM speed envelope (Task 30)` record first (all
rows `NOT VERIFIED`). No frequency or duty value ships in any shipped
configuration.

## Deployment

- One project folder, one command on both platforms: `docker compose up`
  (backend `5080:8080`, frontend `8081:80`).                    [implemented — Tasks 14/20]
- PC uses the device-free base Compose file. A Pi `.env` selects the Pi-only
  overlay with `COMPOSE_FILE`; the overlay merges the same backend service and
  is kept device/group-empty until target inventory verifies values.
                                                               [boundary implemented — Task 24; Pi values NOT VERIFIED]
- No application source edit or second backend service is required to switch
  environments.                                                 [implemented/configuration boundary]
- Backend image: multi-stage, non-root (`app`), framework-dependent and
  architecture-neutral. `linux/arm64` **builds verified** (Task 15); Pi
  **runtime** not yet exercised.                                [implemented / partially verified]
- GPIO/PWM device exposure and permissions: unresolved, owned by Task 24
  (no device paths are claimed anywhere until then).
- Operational rule: `docker compose build` after source changes — `up` reuses
  stale images otherwise (recorded in `docs/DOCKER.md`).

## Testing

- Backend: xUnit suites (Domain/Application/Api via `WebApplicationFactory`
  with in-memory TestServer; hand-written fakes, DI override for error
  mappings). `dotnet test backend/DroneControl.sln`.           [implemented — Task 17]
- Frontend: Vitest + RTL, jsdom; fetch injected as a fake; test setup pins
  `VITE_DRONE_SERVICE=mock`. `npm test` in `frontend/`.        [implemented — Tasks 6/7/18/19]

## Hardware verification levels (normative for Tasks 22+)

```text
design only → implemented → built → mock-tested → dry-run-tested
            → Pi-runtime-verified → real-hardware-verified
```

Acceptance claims must name their level; lower levels never imply higher
ones. Physical unknowns are tracked in the prerequisites ledger of
`specs/architecture/runtime-deployment.md` — nothing in this file asserts
behavior that has not been verified at the stated level.

## Spec index

| Area | Specs |
| --- | --- |
| Frontend | `specs/frontend/*.md` |
| Backend | `specs/backend/*.md` |
| Docker | `specs/docker/*.md` |
| Integration | `specs/integration/*.md` |
| Architecture | `specs/architecture/runtime-deployment.md` |
| Hardware | `specs/hardware/hardware-abstraction.md` (implemented); further `specs/hardware/*.md` (Tasks 23+) |
