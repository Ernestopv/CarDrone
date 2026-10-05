# Unified Docker Compose (Task 34)

## Purpose

Finalize the CarDrone deployment mechanics so that the same project folder
works on a development PC and a Raspberry Pi with one literal command, using
configuration/environment only — no source-code differences, no per-platform
files beyond the folder's `.env`, and no camera/device/privilege requirements
on the PC. This is the deployment task that wires the values Tasks 31–33
deferred to it (`COMPOSE_PROFILES`, `CAMERA_PROBE_URL`,
`CAMERA_PROXY_TARGET`) and validates the final three-service Compose
architecture.

## Dependencies

- `specs/architecture/runtime-deployment.md` (parent design, D2/D5/D6) — the
  mechanism this task completes: one `docker compose up`, platform differences
  via `.env` + compose profiles, the profile name `camera`, the no-new-host-
  port rule, and the prerequisites ledger.
- `specs/hardware/camera-runtime.md` (Task 31) — the deferred values this task
  wires: `Camera:ProbeUrl` form `http://frontend/camera/` and the `.env` /
  `COMPOSE_PROFILES` wiring.
- `specs/docker/ustreamer-container.md` (Task 32) — the `ustreamer` service
  (profile `camera`) and the intended Pi value `CAMERA_PROXY_TARGET =
  http://ustreamer:8080/stream`, applied by this task's `.env`.
- `specs/integration/camera-stream.md` (Task 33) — the browser consumes the
  mode from `CAMERA_PROXY_TARGET`-derived `camera-mode.json`; this task only
  supplies the deployment values.
- `docs/DECISIONS.md` D2/D4/D5/D6 — decision records whose statuses this task
  finalizes where implemented.
- Backlog Task 34 requirements (same structure/command; no per-platform source
  changes; `/api/` + `/camera/` proxying; health checks/ordering where useful;
  ARM64-compatible services; PC mode without Raspberry hardware).

## Scope

- The final Pi configuration surface: `.env.raspberry.example` gains the
  camera/profile values (`CAMERA_MODE`, `CAMERA_PROBE_URL`,
  `CAMERA_PROXY_TARGET`, `COMPOSE_PROFILES`) and documents the copy-to-`.env`
  flow.
- Validation of the final architecture matrices (PC default vs Pi `.env`),
  PC runtime smoke, and the guarantee that applying the Pi `.env` on an
  unsupported platform fails loudly.
- Documentation/ledger finalization: `docs/DOCKER.md` run commands,
  `docs/ARCHITECTURE.md` statuses, `docs/DECISIONS.md` D2/D4/D5/D6 statuses,
  `MEMORY.md` entry, and the camera ledger rows as "wired in Task 34".

## Out of Scope

- Camera device/permission mappings in the Pi overlay (Task 24 inventory,
  Task 36 verification) — seams stay reserved, nothing added.
- Pi-runtime verification of the stack and encoder-default support (Task 36);
  end-to-end real-video validation (Task 38).
- Any application source code change; any change to backend/frontend image
  behavior; changes to services/ports/healthchecks already shipped by Tasks
  20/31/32/33 unless a review proves an ordering fix is required.
- New host ports for the camera (none ever published).

## Architecture

```text
SAME SOURCE TREE
  docker-compose.yml            (base: frontend + backend + ustreamer[profile camera])
  docker-compose.raspberry.yml  (Pi overlay: device/elevation seams ONLY, filled from inventory)
  .env  (PC: absent → pure defaults; Pi: copy of .env.raspberry.example)

PC (no .env)                          RASPBERRY PI (.env from example)
  COMPOSE_PROFILES unset                COMPOSE_PROFILES=camera
  HARDWARE_MODE=mock (default)          HARDWARE_MODE=real (or dry-run)
  CAMERA_MODE=mock  (default)           CAMERA_MODE=ustreamer
  CAMERA__PROBEURL=""  (ignored)        CAMERA__PROBEURL=http://frontend/camera/
  CAMERA_PROXY_TARGET=""  → /camera/404 CAMERA_PROXY_TARGET=http://ustreamer:8080/stream
  started: frontend, backend            started: frontend, backend, ustreamer(camera)
  literal command: docker compose up    literal command: docker compose up (identical)
```

Task 34 adds **no new host port, no device, no privilege, no compose service**
— it configures the values that select the existing graph. The PC remains
device-free and camera-free by construction: the `camera` profile is inactive
and every `CAMERA_*` input defaults to its Task 31/32 empty/mock default.

## Domain Model

Not applicable — deployment configuration only; no application domain types.

## Behavior

- **PC mode (repository as shipped).** No `.env` required. `docker compose up`
  starts `frontend` + `backend`; `/camera/` answers `404` (Task 32), the
  simulated camera UI stays (Task 33 mock mode), and no camera image/container
  is built or run.
- **Raspberry Pi mode.** Copy `.env.raspberry.example` → `.env` on the Pi
  (after the Task 24 inventory evidence exists). `docker compose up` then
  starts all three services; uStreamer runs because `COMPOSE_PROFILES=camera`;
  the backend probes `http://frontend/camera/` (through nginx) and maps the
  real pipeline onto `DroneStatus.camera`; the browser gets MJPEG at
  `/camera/`. Applying this `.env` on a non-`linux/arm64` host must abort at
  backend startup (D5) — never a silent fallback to simulated.
- **Configuration, not code.** The identical literal command and the identical
  checked-in compose files on both platforms; only the folder's `.env` differs
  (plus `docker-compose.raspberry.yml` on the Pi from the same tree).
- **Ordering/health.** `frontend` depends on `backend` being healthy (existing);
  nothing depends on `ustreamer` (control-plane independence, D4);
  no camera-dependent healthcheck is added.

## Interfaces

**`.env.raspberry.example` final content**

```text
COMPOSE_FILE=docker-compose.yml:docker-compose.raspberry.yml
HARDWARE_MODE=real            # or dry-run (bench-safe equivalent)
CAMERA_MODE=ustreamer
COMPOSE_PROFILES=camera
CAMERA_PROBE_URL=http://frontend/camera/
CAMERA_PROXY_TARGET=http://ustreamer:8080/stream
SAFETY_EXTERNAL_FAILURE_PROTECTION_VERIFIED=false
```

Documented in the file: the copy-to-`.env` flow, the inventory prerequisite,
and that `dry-run` is the bench-safe `real` alternative. PC mode is the
absence of a `.env` (pure defaults) — no PC example file is introduced.

**Compile-time passthroughs (already shipped, now fed)**

| Env in composition | Consumed as | Value on the Pi |
| --- | --- | --- |
| `CAMERA_MODE` | `CAMERA_MODE` (backend) | `ustreamer` |
| `CAMERA_PROBE_URL` | `Camera:ProbeUrl` (backend probe, Task 31) | `http://frontend/camera/` |
| `CAMERA_PROXY_TARGET` | frontend `/camera/` location (Task 32) | `http://ustreamer:8080/stream` |
| `COMPOSE_PROFILES` | compose profile activation | `camera` |
| `HARDWARE_MODE` | hardware graph selection | `real` (or `dry-run`) |
| `SAFETY_EXTERNAL_FAILURE_PROTECTION_VERIFIED` | D7 gate | `false` (until externally verified) |

## Validation

1. **PC matrix (default env):** `docker compose config` renders exactly
   `frontend` + `backend` (no `ustreamer`, no devices/privileges, no camera
   host port), `CAMERA_PROXY_TARGET` empty; the rendered output equals today's
   two services plus the already-shipped Task 31/32/33 lines.
2. **Pi matrix (example env):** `docker compose --env-file .env.raspberry.example
   config` renders the three services, `ustreamer` under profile `camera`, the
   four camera keys carrying the intended values, and no device mapping/group/
   privilege/host port.
3. **PC runtime smoke:** `docker compose up` on this machine starts the two
   services healthy: `GET /api/health` → 200, `GET /` → 200, `GET /camera/` →
   404, and no camera container is created; then `docker compose down
   --remove-orphans`.
4. **Pi env on an unsupported platform fails loudly:** applying the Pi `.env`
   values in a `linux/amd64` context aborts backend startup with the explicit
   message (D5); evidenced by the Task 31 platform-gate tests plus a live
   check where cheap.
5. **Regression:** backend and frontend suites unchanged (no source edits).
6. **Config/property checks:** no `docker-compose.yml`/`docker-compose.raspberry.yml`
   diff beyond reserved seams in this task; `docker compose config --profiles`
   lists `camera`.

## Error Cases

| Case | Observable behavior |
| --- | --- |
| Pi `.env` applied on a PC (non-`linux/arm64`) | Backend startup aborts naming `CAMERA_MODE`/`HARDWARE_MODE` and the platform requirement (D5); no mock fallback |
| `CAMERA_PROBE_URL`/`CAMERA_PROXY_TARGET` malformed in the Pi `.env` | Backend strict validation aborts (Task 31); nginx renders the `404` branch for `/camera/` (Task 32) — no raw proxy error |
| uStreamer down on the Pi | `/camera/` 502/504, backend probe → `Error`, `/api/` unaffected (D4); frontend shows honest error and retries (Task 33) |
| PC with `COMPOSE_PROFILES=camera` set by accident | uStreamer containers start but the backend still fails `<ustreamer>` on non-arm64 — caught by the platform gate |
| Missing Pi `.env` | Docker Compose uses defaults → PC behavior on the Pi (mock/mock); the operator sees no camera service (documented) |

## Platform Requirements

- Same tree, same command, same images on PC and Pi; differences only via
  `.env`/profiles.
- PC: no camera, no devices, no privileges, no `.env`.
- Pi: `linux/arm64` images (backend Task 15, camera Task 32, `nginx:1-alpine`
  frontend multi-arch); device mappings only from inventory (seam reserved);
  `docker compose up` (or `-d`).
- Pi container runtime of the whole stack stays `NOT VERIFIED` until Task 36;
  this task validates configuration and build-level guarantees only.

## Security / Safety

- No invented device paths, groups, or privileges; seams stay empty until
  evidence (D6). No `privileged: true`.
- Fail-fast (D5): intent declared in the Pi `.env` is never silently served by
  a different mode.
- Control-plane independence (D4) preserved: nothing depends on the camera.
- No new secrets, no new host-facing exposure; the camera is compose-network-
  internal only.

## Testing Scenarios

1. `docker compose config` with the default environment — PC matrix (1).
2. `docker compose --env-file .env.raspberry.example config` — Pi matrix (2),
   including `--profiles`.
3. PC `docker compose up` smoke and health checks (3).
4. Non-arm64 rejection evidence for the Pi env (4).
5. Full test-suite regression (5) and the no-compose-diff/config-property
   audit (6).

## Acceptance Criteria

- [x] `.env.raspberry.example` carries the final Pi configuration:
      `COMPOSE_FILE`, `HARDWARE_MODE=real` (dry-run documented), `CAMERA_MODE=
      ustreamer`, `COMPOSE_PROFILES=camera`, `CAMERA_PROBE_URL=http://frontend/camera/`,
      `CAMERA_PROXY_TARGET=http://ustreamer:8080/stream`, and the D7 flag at
      `false`, with the copy-to-`.env` and inventory prerequisites documented.
- [x] PC mode is the absence of a `.env`: `docker compose config` renders
      exactly `frontend` + `backend`, no `ustreamer`, no devices/privileges, no
      camera host port, `CAMERA_PROXY_TARGET` empty, and no additional diff
      beyond the shipped Task 31/32/33 lines.
- [x] With the Pi example env, `docker compose config` (and `--profiles`)
      renders the three services with `ustreamer` under profile `camera`, the
      intended camera key values, and no device mapping/group/privilege/host
      port.
- [x] `docker compose up` on this PC starts frontend+backend healthy
      (`/api/health` 200, `/` 200, `/camera/` 404), creates no camera
      container, and `down --remove-orphans` leaves a clean state.
- [x] Applying the Pi `.env` values on a non-`linux/arm64` context aborts
      backend startup loudly (D5) — evidenced by the Task 31 platform-gate
      tests and a live check where cheap; there is no silent fallback.
- [x] No `docker-compose.yml`/`docker-compose.raspberry.yml` change beyond the
      reserved seams appears in this task's diff, and no application source
      file changes; the backend and frontend suites pass unchanged.
- [x] Health-check/dependency ordering is reviewed: `frontend` depends on a
      healthy `backend`; nothing depends on the camera; no camera-dependent
      healthcheck is introduced (control independence, D4).
- [x] The images used are multi-arch capable (backend `arm64`, camera
      `arm64`, frontend `nginx:1-alpine`); full Pi-stack runtime remains
      `NOT VERIFIED` (Task 36).
- [x] Documentation finalization: `docs/DOCKER.md` documents the PC and Pi run
      flows (including the identical `docker compose up` and the Pi `.env`
      flow); `docs/ARCHITECTURE.md` statuses updated; `docs/DECISIONS.md` D2/
      D4/D5/D6 statuses moved to implemented/final where applicable;
      `MEMORY.md` gains the Task 34 entry; no physical claim is added anywhere.
- [x] The Task 31/32 deferred values are recorded as wired in the ledger rows
      (`CAMERA_PROBE_URL` and `CAMERA_PROXY_TARGET` deployment values), with
      Pi runtime still `NOT VERIFIED`.

## Backlog Status

Implemented and validated as configuration-only (Task 34 `COMPLETED`): the Pi
`.env.example` carries the full runtime surface, both `docker compose config`
matrices render correctly, the PC `up` smoke is healthy with no camera
container, the D5 fail-fast behavior is proven live, and backend/frontend
suites are unchanged. Pi-stack runtime remains `NOT VERIFIED` (Task 36).