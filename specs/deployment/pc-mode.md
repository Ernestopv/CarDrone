# PC Runtime Mode (Task 35)

## Purpose

Validate the final application on a Windows/Linux development PC exactly as a
user would run it: copy the project folder, start it with `docker compose up`,
and confirm the full expected matrix — real frontend/backend/API/controls over
the real HTTP contract, with hardware **MOCK** and camera **MOCK** — with no
Raspberry Pi, GPIO, or physical camera involved. This task produces runtime
evidence, not new features.

## Dependencies

- `specs/architecture/runtime-deployment.md` — the PC-vs-Pi matrix this task
  verifies (Frontend/Backend/API real, Hardware/Camera mock on the PC) and the
  mode defaults.
- `specs/docker/unified-compose.md` (Task 34) — the final deployment form this
  task exercises (no `.env` on the PC; `docker compose up`).
- `specs/backend/drone-api.md` + `error-handling.md` — the exact endpoint and
  error contract the checks assert (five endpoints + health; 400/409/503/500
  ProblemDetails).
- `specs/integration/frontend-backend.md` + `frontend-api-service.md` — the
  real `ApiDroneService` on `/api/…` and the same-origin patterns.
- `specs/integration/camera-stream.md` (Task 33) — the mock camera runtime
  expectations on the PC (`camera-mode.json {"mode":"mock"}`, `/camera/` 404).
- Backlog Task 35 requirements block.
- Existing suites (backend 372, frontend 138) as the "no code changed" baseline.

## Scope

- A staged **fresh copy** of the project (deployment-relevant files, excluding
  build artifacts) started with `docker compose up` and no `.env`.
- Runtime assertions over the real stack: SPA served, `/api/health` 200,
  connect/disconnect, directional command, speed change, the offline and
  invalid-input error contracts, camera-mock behaviors, and the two-service
  container inventory.
- Clean shutdown (`down --remove-orphans`) and honest defect reporting.
- Documentation update (MEMORY entry and an ARCHITECTURE status note that PC
  runtime is verified on this host).

## Out of Scope

- Any application source change; a defect found during validation is reported,
  never silently patched (only deployment/configuration defects that are this
  task's own files may be fixed).
- Raspberry Pi runtime (Task 36), end-to-end camera (38), device mappings,
  or any hardware claim — all remain `NOT VERIFIED`.
- Feature work or backlog tasks beyond this validation.

## Architecture

```text
Staged copy of the project (no build artifacts, no .env)
   ↓
docker compose up               (identical literal command; PC defaults)
   ↓
frontend (nginx, host 8081) ── /api/ ──► backend (host 5080, MockDroneController)
   ├── /            SPA            status/camera: simulated (mock)
   ├── /api/health  200            camera-mode.json → {"mode":"mock"}
   └── /camera/     404            /camera/ never SPA HTML
```

The checks are HTTP-level proof that the *real* stack (rendered React SPA,
real nginx, real ASP.NET backend, real `ApiDroneService` path) satisfies the
expected matrix — the same behaviors the unit suites assert at the component
level, now asserted end-to-end over the live compose deployment.

## Behavior

- **Fresh-copy start.** A copy of the repo (source + Dockerfiles + compose +
  `.env.raspberry.example` retained; `node_modules`, `bin`, `obj`, `dist`
  excluded) is placed in a temp folder; `docker compose up` there must build
  and start `frontend` + `backend` without a `.env` and without prompting.
- **Control plane (REAL).** `GET /api/health` → 200 through the frontend
  proxy; the API accepts connect → command → speed → disconnect over the wire,
  and returns the frozen ProblemDetails errors for offline command (409) and
  invalid speed (400).
- **Hardware MOCK.** The backend runs `HARDWARE_MODE` default (mock): no abort,
  no GPIO/Pi code path, `MockDroneController` acknowledges commands as
  simulated (wire ack is never a hardware claim).
- **Camera MOCK.** `camera-mode.json` serves `{"mode":"mock"}`; `/camera/`
  answers 404 (never SPA HTML); the UI camera state follows the simulated
  status (verified by the surrounding frontend suites; HTTP-level here: no
  stream is served).
- **No Pi footprint.** Compose config/`ps` shows only the two services; no
  devices, groups, privileges, or camera container.
- **Cleanup.** `docker compose down --remove-orphans` leaves no containers or
  networks behind.

## Interfaces

No interface changes. The task drives the existing contract:

| Check | Request | Expected |
| --- | --- | --- |
| SPA | `GET /` (frontend) | 200, HTML document |
| Health | `GET /api/health` (via frontend) | 200 |
| Connect | `POST /api/drone/connect` | 200; status `connection: connected` |
| Command | `POST /api/drone/command {"command":"forward"}` | 200; simulated ack; status `confirmedCommand: forward` |
| Speed | `PUT /api/drone/speed {"speed":50}` | 200; status `speed: 50` |
| Offline command | `POST /api/drone/command` while offline | 409 ProblemDetails (unchanged contract) |
| Invalid speed | `PUT /api/drone/speed {"speed":150}` | 400 ProblemDetails (validate: reject, never clamp) |
| Disconnect | `POST /api/drone/disconnect` | 200; `connection: offline` |
| Camera mock | `GET /camera/`, `GET /camera-mode.json` | 404 (non-HTML); `{"mode":"mock"}` |

## Validation

Run on this development PC (Windows + Docker Desktop Linux engine):

1. Stage the fresh copy and `docker compose up`; wait for both health checks.
2. Execute the Interface table checks with `curl` against `localhost:8081`
   and assert each expected value/status.
3. `docker compose ps` pin the two containers and their states; assert no
   camera container.
4. `docker compose config` (from the staged copy) shows no devices/privileges
   and only the two active services.
5. `docker compose down --remove-orphans`; assert no leftover containers.
6. Regression: backend `dotnet test` (372) and frontend `npm test` (138)
   unchanged — evidence the task changed no code.

## Error Cases

| Case | Observable behavior |
| --- | --- |
| Missing `.env` on the PC | Not required — compose uses defaults (asserted in steps 1/4) |
| A service fails to become healthy | Reported as a validation failure; a configuration cause in this task's own files may be fixed, code causes are reported not patched |
| `/camera/` unexpectedly serving content | Fails the camera-mock check; investigated before completing |
| Offline/invalid commands | Must map to the existing 409/400 contract — any other status fails the check |

## Platform Requirements

- Windows or Linux development PC with Docker Engine/Compose v2 (this host).
- `linux/amd64` for the run; no arm64 needed for this task.
- No cameras, no Pi, no GPIO, no `.env`.

## Security / Safety

- The checks never claim hardware actions happened: every acknowledgement on
  the wire is the simulated one the backend produces (honesty rule).
- No code is changed during validation; a reported defect stays visible.
- Cleanup guarantee: no containers/networks left after `down`.

## Testing Scenarios

1. Fresh-copy build/start without `.env` (Validation 1).
2. End-to-end control-plane sequence over the real API (Validation 2).
3. Camera-mock and `/camera/` 404 assertions (Validation 2).
4. Container inventory and config-property audit (Validation 3–4).
5. Clean shutdown (Validation 5) and regression (Validation 6).

## Acceptance Criteria

- [x] A staged fresh copy of the project (no build artifacts, no `.env`) runs
      `docker compose up` and starts exactly `frontend` + `backend`, both
      healthy, on this PC.
- [x] `GET /` serves the built SPA (200) and `GET /api/health` returns 200
      through the frontend proxy — frontend, backend, and API are REAL.
- [x] `POST /api/drone/connect` returns 200 and the API status reports
      `connection: connected`; `POST /api/drone/command` with `forward`
      returns the simulated ack (never a hardware claim) and the status
      reflects requested/confirmed commands — controls are real against the
      mock-backed backend (HARDWARE MOCK).
- [x] `PUT /api/drone/speed` with 50 updates the status; an out-of-range value
      returns `400` ProblemDetails (reject, never clamp); a command while
      offline returns `409` — the frozen error contract holds over the wire.
- [x] `POST /api/drone/disconnect` returns 200 and the status returns to
      `offline`.
- [x] Camera MOCK at runtime: `/camera-mode.json` serves `{"mode":"mock"}` and
      `/camera/` answers 404 with a non-HTML body (never the SPA fallback).
- [x] `docker compose ps`/`config` show only the two services with no device,
      group, privilege, or camera host port, and no camera container is ever
      created; PC mode requires no Raspberry Pi, GPIO, or physical camera.
- [x] `docker compose down --remove-orphans` leaves no containers or networks.
- [x] No application source file is changed during this task: the backend (372)
      and frontend (138) suites pass unchanged, and any defect found is
      reported rather than silently patched.
- [x] Documentation: `MEMORY.md` records the PC-runtime verification and
      `docs/ARCHITECTURE.md` gains a PC-runtime status note; no hardware
      verification claim is added (Pi/camera remain `NOT VERIFIED` — Tasks
      36/38).

## Backlog Status

Validated end-to-end on this development PC (Task 35 `COMPLETED`): the staged
fresh copy started with the literal `docker compose up`, the control plane
checked real over the wire (connect/command/speed/400/409), camera and
hardware are mock (404 + `{"mode":"mock"}`), the stack is device-free and
down-clean, and no source changed. Pi/camera runtime remain `NOT VERIFIED`
(36/38).