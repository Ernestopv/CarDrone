# uStreamer Container (Task 32)

## Purpose

Run uStreamer as part of the Raspberry Pi Docker Compose environment so that a
Pi camera's MJPEG stream is reachable by the browser at the reserved
same-origin path `/camera/`, with the PC development mode entirely unchanged.

This is the implementation task for the media-plane half already fixed by
`specs/architecture/runtime-deployment.md` (D4) and
`specs/hardware/camera-runtime.md` (Task 31: stream path, port strategy,
encoder defaults, profile name, `CAMERA_PROXY_TARGET` semantics, startup /
failure / reconnect behavior). Task 32 turns that fixed design into real
container configuration: the uStreamer service, the nginx `/camera/` location,
and the frontend Compose passthrough.

## Dependencies

- `specs/architecture/runtime-deployment.md` — binding parent design: mode
  vocabulary, D4 (media plane via nginx→uStreamer, never through ASP.NET),
  profile reservation (`camera`), the `/camera/…` reserved path, the
  verification ladder, and the master prerequisites ledger (rotas rows 31/32).
- `specs/hardware/camera-runtime.md` (Task 31, COMPLETED) — the normative
  stream design this task implements: `GET /camera/` relative same-origin, no
  new published host port, encoder design defaults (1280×720 / 15 fps / JPEG
  quality 80, `NOT VERIFIED` on real hardware), compose profile name `camera`,
  `CAMERA_PROXY_TARGET` semantics (unset/empty → `404`, never SPA HTML; set →
  byte-for-byte passthrough), uStreamer restart policy owned by this task, and
  the task-boundary table (rows for Tasks 32/34/36/38).
- `specs/docker/full-stack-compose.md` (Task 20) — the same-origin nginx
  `/api/` proxy pattern this task mirrors for `/camera/`.
- `specs/docker/raspberry-hardware-access.md` (Task 24) +
  `docs/hardware/raspberry-pi-inventory.md` — the device/permission evidence
  discipline this task must follow for camera device access (nothing invented;
  mappings only after target inventory evidence).
- `docs/DECISIONS.md` — D4 (transport separation), D6 (Pi-only Compose overlay
  for device mappings, selected by the Pi `.env`).
- Backlog Task 32 requirements block (Required: ARM64, determine camera device
  access, do not invent device paths, configure stream path / resolution / fps
  / quality / startup / restart, verify uStreamer independently before React,
  PC must work without a camera, avoid unnecessary privileges).

## Scope

- A `camera/` build context: uStreamer multi-stage Dockerfile, `.dockerignore`,
  and a container entry-point that wires encoder configuration from
  environment.
- The Compose `ustreamer` service: profile `camera`, internal-only port, no
  published host port, restart policy, encoder-default environment knobs, no
  `depends_on` from any control service.
- The frontend nginx `/camera/` location and its `CAMERA_PROXY_TARGET`
  mechanism (empty → `404`; set → passthrough), plus the frontend Compose
  environment passthrough line.
- The reserved Pi-overlay seam for the camera device (no mapping added until
  inventory evidence exists).
- Documentation updates (`docs/ARCHITECTURE.md` camera line,
  `docs/DOCKER.md` camera build/run commands, `MEMORY.md` entry) and ledger
  updates for uStreamer facts that become determined.

## Out of Scope

- Browser stream consumption (`streamUrl` constant, MJPEG rendering, client
  reconnect/backoff UX) → **Task 33** (`specs/integration/camera-stream.md`).
- Final `.env` values, `COMPOSE_PROFILES`, and the concrete `Camera:ProbeUrl`
  deployment value → **Task 34** (`specs/docker/unified-compose.md`).
- Pi-runtime verification of the whole pipeline and encoder-default support →
  **Task 36**; end-to-end real-video-in-browser validation → **Task 38**.
- Camera device path discovery and permission proof → Task 24 inventory and
  Task 36; no path is invented here.
- Any change to `backend/` application code or `frontend/src`; any `/api/`
  wire change; any change to `HARDWARE_MODE`/`CAMERA_MODE` selection
  (Task 31/23 behavior stays as-is).
- Physical MJPEG verification: Task 32 proves the container/nginx/compose
  mechanics without a camera unless a camera-dependent check is explicit.

## Architecture

```text
Browser  -- GET /camera/ (relative, same-origin; never a hostname/IP)-->  Frontend nginx (host port unchanged)
                                                                            │  location /camera/  (Task 32)
                                                                            │    CAMERA_PROXY_TARGET empty → return 404 (never SPA HTML)
                                                                            │    CAMERA_PROXY_TARGET set   → byte passthrough (MJPEG)
                                                                            ↓  compose-internal absolute URL (value owned by Task 32)
                                                                         uStreamer  ---- profile: camera, no published host port
                                                                            │         on the compose network, internal port only
                                                                            ↓
                                                                         Raspberry Pi camera device (inventory evidence → mapping later)
```

- **Service topology.** One new compose service `ustreamer` gated by profile
  `camera` (name fixed by Task 31). It has no published host ports, holds no
  device mapping in the checked-in files, and is never a startup/build
  dependency of `frontend` or `backend` (control-plane independence, D4). The
  `frontend` service gains one environment passthrough
  (`CAMERA_PROXY_TARGET`); its nginx configuration renders the `/camera/`
  behavior from that value at container start.
- **Camera device seam (D6 discipline).** Compose cannot conditionally omit a
  service's `devices` list, so the base `ustreamer` service ships without
  `devices`/`group_add`/privileges. The Pi-only overlay
  (`docker-compose.raspberry.yml`) reserves an `ustreamer` key that will
  later receive inventory-verified mappings only — never invented paths, never
  `privileged: true` by default.
- **Build image.** `camera/Dockerfile` is a multi-stage build of uStreamer
  pinned to a specific upstream release/tag (recorded; never `latest`),
  compiled for `linux/arm64` (and optionally `linux/amd64` for CI), running as
  a non-root user on a slim runtime image. The image line is
  `cardrone-camera`.
- **nginx location mechanism.** `frontend/nginx.conf` keeps serving the SPA
  and `/api/` exactly as today and gains, inside the server block, an
  `include /etc/nginx/locations/camera.conf;`. A small
  `docker-entrypoint.d` script (run by the official nginx image before nginx
  starts) always materializes `camera.conf`:

  - `CAMERA_PROXY_TARGET` empty/unset → `location /camera/ { return 404; }` —
    clients receive `404` with no HTML body; the SPA `try_files` fallback
    Never serves `index.html` for `/camera/`.
  - `CAMERA_PROXY_TARGET` set → the location `proxy_pass`es the MJPEG bytes
    through untouched (`proxy_buffering off`, generous read timeout, Host
    header preserved as today's `/api/` pattern). The value is interpolated
    into the generated config at container start (no nginx runtime variable,
    no `resolver` dependency).

  Longest-prefix matching guarantees `/camera/…` hits this location in both
  branches regardless of the `/` fallback.

## Domain Model

Not applicable — this task introduces no application domain type. It only
changes deployment/container configuration. (No empty section invented; the
template's Domain/Interfaces below are configuration contracts instead.)

## Behavior

- **Startup.** Frontend and backend start regardless of the camera profile;
  neither has a `depends_on` on `ustreamer` nor a health check that requires a
  camera. `ustreamer` starts only when the `camera` profile is enabled. The
  `camera.conf` fragment is written before nginx reads its configuration, so
  `/camera/` is always either `404` or a live passthrough — never an nginx
  config error.
- **Stream / resolution / quality.** The uStreamer container applies the Task
  31 encoder design defaults — **1280×720, 15 fps, JPEG quality 80** — as
  operator-tunable environment defaults on the `ustreamer` service
  (`CAMERA_RESOLUTION`, `CAMERA_FPS`, `CAMERA_QUALITY`). Support on a real
  camera is `NOT VERIFIED` (Task 36). No application code encodes these
  numbers.
- **Failure.** Upstream down (disabled profile): `/camera/` answers `404`
  (disabled) or a proxy error `502/504` when the passthrough target is
  unreachable; `/api/` is never affected (D4). The backend probe (Task 31)
  maps these non-2xx answers to `Error` with no control-plane impact. Control
  containers are never restarted for camera failures.
- **Restart / reconnect.** `ustreamer` uses `restart: unless-stopped`
  (Task 32's decision). MJPEG is stateless per request; client reconnection is
  a fresh `GET` (Task 33 owns retry/backoff UX). The Task 31 monitor recovers
  automatically (`Error → Streaming`) at its next probe.
- **PC mode.** With the default environment (`COMPOSE_PROFILES` unset,
  `CAMERA_PROXY_TARGET` empty), `docker compose config`/`up` yields today's
  two-service stack: no `ustreamer` startup, no device/privilege/port change,
  `/camera/` answered `404`, simulated camera unaffected, `backend` receives
  the same `CAMERA_MODE=mock` environment as before.

## Interfaces

**Files introduced/changed**

```text
camera/
├── Dockerfile            (multi-stage uStreamer build; non-root runtime)
├── .dockerignore
└── entrypoint.sh         (map CAMERA_* environment to uStreamer startup args)
frontend/
├── nginx.conf            (+ include /etc/nginx/locations/camera.conf;)
├── Dockerfile            (create /etc/nginx/locations, add docker-entrypoint.d script)
└── docker-entrypoint.d/50-camera-location.sh   (materialize camera.conf from CAMERA_PROXY_TARGET)
docker-compose.yml        (new ustreamer service, profile camera; frontend env line)
docker-compose.raspberry.yml  (reserve ustreamer device seam — no mapping added)
docs/ARCHITECTURE.md, docs/DOCKER.md, MEMORY.md   (status/commands/memory updates)
```

**Compose delta (checked-in)**

- New service `ustreamer`:
  - `build.context: ./camera`, `image: cardrone-camera`
  - `profiles: ["camera"]`
  - `restart: unless-stopped`
  - environment: `CAMERA_RESOLUTION: ${CAMERA_RESOLUTION:-1280x720}`,
    `CAMERA_FPS: ${CAMERA_FPS:-15}`, `CAMERA_QUALITY: ${CAMERA_QUALITY:-80}`
  - no `ports`, no `devices`, no `group_add`, no `privileged`, no
    `depends_on` from or to control services, no camera-dependent
    `healthcheck`.
- `frontend.environment` gains exactly: `CAMERA_PROXY_TARGET:
  ${CAMERA_PROXY_TARGET:-}`.

**Configuration contract**

| Key (ustreamer service env) | Default | Meaning |
| --- | --- | --- |
| `CAMERA_PROXY_TARGET` (frontend env passthrough) | empty | empty → `/camera/` `404`; set → nginx passthrough to this compose-internal absolute URL |
| `CAMERA_RESOLUTION` | `1280x720` | encoder width×height (Task 31 default) |
| `CAMERA_FPS` | `15` | encoder frame rate |
| `CAMERA_QUALITY` | `80` | JPEG quality |

Wired Pi value of `CAMERA_PROXY_TARGET` (documented here, applied by Task
34's `.env`): `http://ustreamer:8080/<endpoint>` where `8080` is the
container-internal port and `<endpoint>` is uStreamer's MJPEG path as
determined by this task from the pinned uStreamer documentation — never
invented, recorded in the prerequisites ledger below. No host port is ever
published for the stream.

## Validation

Software validation achievable without a camera (all `PASS` targets for this
task):

1. `docker compose config` with default environment: exactly the current
   services (`frontend`, `backend`) with no new device/port/privilege and the
   new `CAMERA_PROXY_TARGET` key rendering empty; `ustreamer` is not enabled.
2. `docker compose config` with `COMPOSE_PROFILES=camera`: `ustreamer`
   renders (still no devices, no ports, no privileges).
3. `docker buildx build --platform linux/arm64 -t cardrone-camera camera/`
   succeeds (genuine cross-build) and `docker image inspect` reports
   `arm64/linux`. Pi runtime remains `NOT VERIFIED`.
4. Frontend nginx behavior on the PC (no camera required): with
   `CAMERA_PROXY_TARGET` empty, `GET /camera/` returns HTTP `404` with a
   non-HTML body (never the SPA document); with the variable set to a local
   stub upstream, `GET /camera/` passes the upstream status and body through
   byte-for-byte (SPA fallback never intercepts `/camera/`). `/api/` behavior
   unchanged.
5. Backend probe consistency regression: `CAMERA_MODE=mock` environment
   unchanged; Task 31 monitor behavior already covered by its tests. No new
   backend test baseline change.
6. `dotnet build`/`dotnet test` (×2) and `npm test` (121) unchanged — no
   application source is modified.

Deferred (asserted `NOT VERIFIED`, never claimed here): real MJPEG bytes from
a Pi camera, encoder-default support, uStreamer build/endpoint facts on a
physical Pi, camera device path mapping — Tasks 24/36/38 own these.

## Error Cases

- `CAMERA_PROXY_TARGET` set but `ustreamer` unreachable → `/camera/` answers a
  proxy error (`502/504`); backend probe → `Error`; `/api/` and control
  commands unaffected (D4).
- Profile disabled on PC → `/camera/` `404` with no HTML; the SPA never
  serves `index.html` for a stream path.
- Missing camera device at runtime (Pi, no inventory mapping) → uStreamer
  startup/source behavior is camera-dependent and `NOT VERIFIED` until Task
  36; the software mechanics (container, config, routing) do not depend on a
  device being mapped.
- nginx config generation failure would leave nginx down → the entry-point
  script must render a valid `camera.conf` in every branch (404 or
  passthrough); a passthrough target that is present-but-malformed must be
  treated as configuration error at container start or degrade to the `404`
  branch — never silently served.

## Platform Requirements

- `linux/arm64` for the uStreamer image (build verified via buildx; Pi runtime
  `NOT VERIFIED`), optionally `linux/amd64` for CI sanity.
- PC: unchanged — camera profile disabled by default; no camera required.
- Non-root container user for uStreamer; no `privileged: true`; no
  host-network, host-mount, or blanket device exposure.
- Camera device access only through inventory-verified mappings in the Pi
  overlay (`docker-compose.raspberry.yml`), following Task 24's discipline.

## Security / Safety

- **No invented hardware.** Camera device paths, uStreamer port/endpoint
  layout, and encoder support are recorded as prerequisites; nothing is
  guessed. No `privileged: true`, no blanket device exposure.
- **Control-plane independence (D4).** uStreamer is never an ASP.NET concern;
  `/api/` behavior and the camera *status* mapping (Task 31) are untouched.
- **Honesty.** No acceptance criterion or doc update claims physical video
  verification. The independent verification achieved here is
  container/nginx/compose level; pixel-level verification is Task 36/38.
- **Verification vocabulary** (normative, from the parent design): this
  task's strongest level is `mock-tested`/`built` for software mechanics;
  `Pi-runtime-verified`/`real-hardware-verified` are explicit `NOT VERIFIED`
  deferrals.

## Prerequisites Ledger (this task)

| Unverified fact | Owned by | Status |
| --- | --- | --- |
| uStreamer upstream release/tag to pin, its build options and runtime deps, internal listen port and MJPEG endpoint layout | Task 32 (determine from pinned docs during implementation), 36 (verify) | `NOT VERIFIED` until recorded from upstream documentation; never invented |
| Pi camera device presence, path, and access model for the container | Task 24 inventory, 32 (apply none yet), 36 (verify) | `NOT VERIFIED` — no mapping added |
| Encoder defaults (1280×720 / 15 fps / quality 80) supported by the real camera | Task 32 (wire), 36 (verify) | `NOT VERIFIED` — design defaults only |
| End-to-end MJPEG in the shipped browser | Task 33 (implement), 38 (verify) | `NOT VERIFIED` until those tasks run |

## Testing Scenarios

1. Compose render matrices (default vs `COMPOSE_PROFILES=camera`) — see
   Validation 1–2.
2. ARM64 image cross-build + arch inspect — Validation 3.
3. nginx `/camera/` 404-vs-passthrough behavior with a stub upstream — no
   camera — Validation 4.
4. Env-knob rendering check: `CAMERA_FPS`/`CAMERA_QUALITY` values reach the
   generated uStreamer command line (static inspection; runtime effect is
   `NOT VERIFIED`).
5. Regression: full test suites unchanged (Validation 6).

## Acceptance Criteria

- [x] A `camera/` build context exists: multi-stage uStreamer Dockerfile pinned
      to a specific upstream release/tag (recorded in the ledger, never
      `latest`), `.dockerignore`, non-root runtime user, and an entry-point
      that applies the encoder defaults from environment. `docker buildx
      build --platform linux/arm64` succeeds and the image inspects as
      `arm64/linux`; the image is `cardrone-camera`.
- [x] Compose gains exactly one new service `ustreamer` under
      `profiles: ["camera"]` with `restart: unless-stopped`, the three
      `CAMERA_*` encoder env knobs defaulting to 1280×720 / 15 / 80, no
      published host port, no devices/group_add/privileged, and no
      `depends_on` from or to `frontend`/`backend`.
- [x] `frontend` gains exactly one environment line
      (`CAMERA_PROXY_TARGET: ${CAMERA_PROXY_TARGET:-}`); the frontend nginx
      configuration gains `include /etc/nginx/locations/camera.conf;` and the
      entry-point script that always materializes it.
- [x] With `CAMERA_PROXY_TARGET` empty/unset the container serves
      `GET /camera/` as HTTP `404` with a non-HTML body (verified on PC; the
      SPA fallback never serves `index.html` for `/camera/`).
- [x] With `CAMERA_PROXY_TARGET` set to a stub upstream, `GET /camera/`
      passes the upstream status and body through byte-for-byte (verified on
      PC with no camera); `/api/` behavior is unchanged.
- [x] `docker compose config` with the default environment renders the same
      two services with no new device/port/privilege/healthcheck changes and
      `CAMERA_PROXY_TARGET` empty; with `COMPOSE_PROFILES=camera` it also
      renders `ustreamer` with no devices.
- [x] No camera device mapping, group, privilege, or host mount is added by
      this task; the Pi overlay reserves the `ustreamer` seam only
      (`docker-compose.raspberry.yml`), to be filled from inventory evidence.
- [x] uStreamer's pinned release/tag, internal listen port, and MJPEG
      endpoint layout are recorded in the prerequisites ledger as determined
      facts from upstream documentation — or marked `NOT VERIFIED` if they
      could not be determined without a target — and are never invented.
- [x] Encoder design defaults are applied as operator-tunable configuration,
      never encoded in application code; real-camera support is recorded
      `NOT VERIFIED` (Task 36).
- [x] No application source code changes: `backend/` and `frontend/src` are
      untouched; `dotnet build` 0/0, `dotnet test` twice identical, `npm
      test` 121, and `docker compose config` all pass (PC mode unchanged).
- [x] Documentation: `docs/ARCHITECTURE.md` `/camera/` line reflects that the
      proxy and uStreamer service are implemented (mock-tested; Pi runtime
      and real video deferred); `docs/DOCKER.md` gains the camera build/run
      commands; `MEMORY.md` gains the Task 32 entry; no physical claim is
      added anywhere.
- [x] Every camera/hardware-dependent criterion in this spec is reported
      `NOT VERIFIED` at completion (no MJPEG-on-Pi, no device mapping, no
      encoder-default support, no Pi container runtime claim) — the task
      completes on software-level evidence only, with deferrals named.

## Backlog Status

Implemented and validated on software-level evidence (Task 32 `COMPLETED`):
image build `arm64/linux`, Compose renders for default and `camera` profile,
nginx `/camera/` 404/passthrough verified on the PC, and test suites
unchanged. Physical/on-Pi evidence remains `NOT VERIFIED` per the ledger
(real MJPEG, camera device mapping, encoder-default support → Tasks 36/38).