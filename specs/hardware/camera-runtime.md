# Camera Runtime (Task 31)

## Purpose

Task 31 defines and implements the camera runtime for both platforms: how
`CAMERA_MODE` is selected and validated at the composition root, how the real
stream-pipeline health is mapped honestly onto the existing
`DroneStatus.camera` field, and — as normative design — the complete stream
strategy (URL, path, port, resolution, frame rate, quality, startup, failure,
reconnect) that Tasks 32–34 implement on the transport side.

```text
CONTROL PLANE (unchanged)                    MEDIA PLANE (designed here)

Browser ── /api/ ──► Backend API              Browser ── GET /camera/ ──► Frontend nginx
                         │                                              │  location /camera/
                         │  CAMERA_MODE=mock:  simulator state           │    empty target → 404
                         │  CAMERA_MODE=ustreamer:                       │    set target  → proxy
                         │    CameraStatusMonitor (outbound probe)       ▼
                         │         ↓ cached CameraStatus            uStreamer  [profile: camera]
                         │    CameraStatusOverlay (decorator)            │
                         │         ↓ remaps State.Camera only            ▼
                         └──► DroneStatus.camera                     Pi Camera
                              (wire: offline|connecting|streaming|error)
```

Two halves, one boundary:

- **Implemented by this task (backend half):** `CAMERA_MODE` parsing and
  strict validation, the Raspberry platform gate for `ustreamer`, the
  `Camera:ProbeUrl` configuration contract, the probe-backed status mapping,
  and the two backend environment passthrough lines in `docker-compose.yml`.
- **Defined by this task, implemented by Tasks 32/33/34 (transport half):**
  the `/camera/` stream path and its nginx behavior, the "no new host port"
  rule, the uStreamer encoder design defaults, the compose profile name, and
  the startup/failure/reconnect semantics of the pipeline. The task-boundary
  table under [Stream Design](#stream-design-normative--implementation-owners)
  records who implements what; nothing outside this task's column is coded
  here.

The task touches **no video bytes**: ASP.NET never proxies, relays, buffers,
or inspects the stream (decision D4); the probe is a headers-only HTTP request
whose response body is never read. The task writes **no frontend code** and
changes **no wire shape**.

Verification level delivered by this task: `implemented` / `built` /
`mock-tested` on the development PC — configuration validation, mapping, and
probe behavior proven against in-memory/stub HTTP endpoints. Nothing
Raspberry-Pi-runtime, nothing end-to-end video, nothing physical is claimed
(prerequisites remain owned by the ledger and Tasks 32/36/38).

## Dependencies

- `AGENTS.md` — camera placeholder/simulated-telemetry honesty; "the frontend
  must never visually report success without real confirmation"; no invented
  hardware behavior; hardware values from configuration, never hardcoded.
- `specs/architecture/runtime-deployment.md` (binding) — decisions D3
  (composition-root-only selection), D4 (media plane via nginx→uStreamer, the
  backend never proxies video, camera status arrives through the unchanged
  `DroneStatus.camera` field, "Task 31 defines the mapping"), D5 (unknown or
  invalid `CAMERA_MODE` aborts startup; control must remain functional when
  the stream is unavailable), the mode-vocabulary table (`CAMERA_MODE = mock
  (default) | ustreamer`, unsets→`mock`, empty/unknown→abort), the
  task-mapping row that assigns this task "Camera runtime + mapping of real
  camera status onto `DroneStatus.camera`", the compose-profile reservation
  ("`camera` … names fixed by Tasks 31/32"), and the prerequisites ledger
  (uStreamer port/endpoint layout jointly owned by Tasks 31/32; frontend stream
  consumption owned by Task 33).
- `docs/DECISIONS.md` — D3, D4, D5 in durable form.
- `specs/frontend/camera.md` — the four camera states
  (`offline|connecting|streaming|error`) the UI already represents; they are
  reused unchanged, no fifth state is introduced.
- `specs/frontend/drone-simulator.md` — today's simulator-driven camera
  behavior, which must stay byte-identical when `CAMERA_MODE` is unset or
  `mock`.
- `specs/docker/full-stack-compose.md` — the same-origin nginx proxy pattern
  the `/camera/` location mirrors (`/api/` → `http://backend:8080`), compose
  service DNS, and host-port convention (backend `5080:8080`, frontend
  `8081:80`); "no camera service" is explicitly out of that task's scope and
  stays out of scope here.
- `specs/hardware/raspberry-hardware-provider.md` — the `linux/arm64`
  platform preflight (`IsRaspberryRuntime`), its OS-facts-only rule, the
  `HostAbortedException` message shape, and the internal
  `raspberryRuntimeSupported` test seam (exposed via `InternalsVisibleTo`)
  that this task mirrors for the camera gate.
- `specs/hardware/failsafe.md` (Task 26) — the safety decorator forces
  `Camera = CameraStatus.Offline` whenever the safety path is closed; the
  camera overlay must be composed **below** it so that override always wins.
  Task 31 adds no safety transition and changes none.
- `specs/backend/drone-api.md` — the frozen wire: five drone endpoints plus
  health, string enums in camelCase, `camera` as one of the existing fields.
- `specs/backend/backend-tests.md` and the dotnet skill — test placement
  (composition/selection → Api tests; Infrastructure seams → Application
  tests, as `SpeedControllerTests`/`DryRunPwmControllerTests` are placed
  today).
- The docker skill — compose environment passthrough conventions
  (`${VAR:-default}`) and the "no new ports/privileges/devices" discipline.
- The `raspberry-pi` skill is **not available** in this environment (loading
  it fails; precedent set in `hardware-abstraction.md`). Hardware rules are
  enforced through this specification, `AGENTS.md`, and the prerequisites
  ledger instead. No physical camera behavior is inferred anywhere below.
- Backlog: Task 31 (this spec), Task 32 (`specs/docker/ustreamer-container.md`),
  Task 33 (`specs/integration/camera-stream.md`), Task 34 (unified compose),
  Tasks 36/38 (Pi runtime and end-to-end camera verification).

## Scope

Infrastructure + composition root + two compose environment lines:

- `CameraMode` enum and `CameraModeKey = "CAMERA_MODE"` parsing at the
  composition root, mirroring `DroneRuntimeSelection.ParseMode` exactly
  (unset → default; present-but-empty is invalid; case-insensitive trimmed
  values; unknown → `HostAbortedException` naming the key and accepted
  values; no silent fallback — D5).
- The Raspberry platform gate for `ustreamer` (`linux/arm64`, OS facts only,
  no device probing), reusing the internal platform-seam test pattern.
- The `Camera:ProbeUrl` configuration contract: required, non-empty, absolute
  `http://`/`https://` URL **iff** `CAMERA_MODE=ustreamer`; never read in
  `mock` mode; no shipped default anywhere (appsettings ships no `Camera:`
  section).
- `CameraStatusMonitor` (Infrastructure, hosted): a singleton that probes the
  configured URL on a fixed software cadence with a fixed per-attempt timeout,
  publishes the cached `CameraStatus`, and logs transitions only.
- `CameraStatusOverlay : IDroneController` (Infrastructure): a decorator that
  delegates every operation and every other state field unchanged and
  remaps **only** `State.Camera` from the monitor cache on status reads.
- Composition wiring: `AddCameraRuntime(...)` entry point returning the
  resolved mode; `Program.cs` applies the overlay to the inner controller
  **inside** the `DroneSafetyController` factory (safety stays outermost), via
  an internal testable helper.
- `docker-compose.yml` backend environment gains exactly two passthrough
  lines (`CAMERA_MODE` default `mock`, `CAMERA__PROBEURL` default empty) with
  explanatory comments in the `HARDWARE_MODE`/`SAFETY__` style. Defaults
  reproduce today's behavior exactly.
- Normative stream design (path, port, URL strategy, encoder design defaults,
  startup/failure/reconnect behavior, profile name, `CAMERA_PROXY_TARGET`
  semantics) with the task-boundary table — design content only; no nginx,
  Dockerfile, profile, or uStreamer code in this task.
- Comment/doc corrections enumerated under Validation: the
  `HardwareDroneController` camera comment, the `Program.cs` posture comments,
  `docs/ARCHITECTURE.md` (`CAMERA_MODE` "remains design" status lines), and a
  `MEMORY.md` entry.

## Out of Scope

- uStreamer itself: container/image, ARM64 build, camera device access,
  internal listen port, MJPEG endpoint layout, encoder configuration wiring,
  restart policy, profile attachment (Task 32; values defined here but never
  invented as facts).
- The nginx `/camera/` location implementation and its `CAMERA_PROXY_TARGET`
  mechanism (Task 32 implements; defined under
  [Stream Design](#stream-design-normative--implementation-owners)).
- Any React/frontend change: `CameraView`, `streamUrl` consumption, MJPEG
  rendering, reconnection UX (Task 33). Zero frontend files are touched; the
  frontend test suite stays at 121.
- `.env` files, `COMPOSE_PROFILES` wiring, and the deployment value of
  `Camera:ProbeUrl` (Task 34 documents and wires them; this spec fixes the
  value's required form).
- New API surface: no new endpoint, no camera health route, no wire field,
  no SignalR/WebSocket/WebRTC, no second status channel.
- Video content: no frame, pixel, JPEG, or body byte is ever read, buffered,
  inspected, or relayed by the backend — only response headers.
- Backend proxying of video, or any uStreamer↔ASP.NET coupling beyond the
  outbound probe request.
- Pi runtime execution and real-video verification (Tasks 36/38); camera
  device paths and privilege configuration (Tasks 24/32).
- Safety transitions, session lifecycle, command flow (Tasks 23/26 reused
  as-is).
- Any claim that the camera hardware supports the design defaults below.

## Architecture

Preserved layering:

```text
React (unchanged)                          ← camera states via existing wire only
ASP.NET Core API / Application             ← unchanged (no camera vocabulary added)
DroneSafetyController (Task 26 decorator)  ← outermost; forces Offline when closed
   └── CameraStatusOverlay (Task 31)       ← ustreamer only; remaps State.Camera
          └── MockDroneController / HardwareDroneController   ← untouched behavior
CameraStatusMonitor (hosted, Infrastructure) → outbound GET Camera:ProbeUrl
```

- Selection lives at the composition root only (D3): `CameraMode` is read
  once at startup; nothing switches camera mode while the process runs; Domain
  and Application never branch on camera mode.
- **Composition order is a contract:** `DroneSafetyController` wraps
  `CameraStatusOverlay`, which wraps the inner controller. The safety-closed
  `Camera = Offline` override therefore always wins over the probe value.
  `Program.cs` captures the mode returned by `AddCameraRuntime` exactly as it
  captures `hardwareMode` today, and applies an internal helper
  (`CameraRuntimeSelection.ApplyCameraOverlay(inner, mode, services)`) inside
  the existing safety factory — the helper returns `inner` unchanged in
  `mock` mode, so the mock graph is structurally untouched.
- Vocabulary isolation (greppable rule): `CAMERA_MODE`, `ProbeUrl`,
  `CameraStatusMonitor`, `CameraStatusOverlay`, and `uStreamer` never appear
  in Domain, Application, API controllers, or the frontend; the only camera
  vocabulary Domain keeps is the pre-existing `CameraStatus` enum.
- The monitor is the only component that performs network I/O for the camera,
  it runs only in `ustreamer` mode, and its traffic is outbound HTTP to an
  operator-supplied URL — the media plane itself never enters ASP.NET (D4).

## Domain Model

### `CameraStatus` (existing Domain enum — reused, frozen)

`Offline`, `Connecting`, `Streaming`, `Error`. **No new values**; the wire
keeps serializing them as camelCase strings
(`offline|connecting|streaming|error`) via the existing
`JsonStringEnumConverter`. The frontend's
`CameraStatus = 'offline' | 'connecting' | 'streaming' | 'error'` keeps
matching.

### `CameraMode` (composition root, Api)

| Member | Meaning |
|---|---|
| `Mock` | Simulated camera status (today's exact behavior; default when `CAMERA_MODE` is unset). |
| `Ustreamer` | Real pipeline: camera status comes from the probe-backed monitor. |

Read from configuration key `CAMERA_MODE` (env) by `CameraModeKey`, parsed
once at startup in the `ParseMode` style.

### `Camera:ProbeUrl` (configuration, no shipped value)

| Property | Rule |
|---|---|
| Presence | Required iff `CAMERA_MODE=ustreamer`; never read when the mode is `mock`. |
| Type | Absolute `http://` or `https://` URL (RFC-absolute); anything else is a startup error. |
| Default | None ships: `appsettings.json` contains no `Camera:` section and compose ships an empty default that is simply ignored in `mock` mode. |
| Meaning | The full URL of the **public camera stream path as reachable from inside the compose network** — the same path the browser uses. The intended deployment value is `http://frontend/camera/` (existing compose service DNS `frontend`, its container port `80`, the parent-reserved `/camera/` path — composed only of existing facts; Tasks 32/34 document and wire it). |
| Invariant | The probe path must equal the browser-facing stream path (`/camera/`): the probe observes what a browser would reach, nothing else. |

### `CameraStatusMonitor` (Infrastructure, hosted singleton)

- Owns the probe loop: **first attempt immediately at host start**, then a
  delay of **5 seconds** between attempts; **3-second** per-attempt timeout.
  Both are fixed software constants documented here (not electrical or camera
  facts, not configuration in this task).
- Probe request: `GET` to the configured absolute URL with
  `HttpCompletionOption.ResponseHeadersRead` — **success = HTTP status 2xx
  within the timeout**; timeout, connection refusal, DNS failure, non-2xx, or
  cancellation = failure. The response is disposed **without reading the
  body**; no video byte is ever buffered.
- Publishes `Current` (`CameraStatus`), updated atomically after each attempt:
  initial value `Connecting` (first attempt not yet completed), then
  `Streaming` on success / `Error` on failure, including automatic recovery
  `Error → Streaming` (no latch, no manual reset).
- Cancellation: host shutdown cancels any in-flight attempt and delay
  promptly; shutdown-time cancellation is not logged as an error.
- Logging: **transitions only** (entering `Streaming` at Information,
  entering `Error` at Warning) — repeated failures of the same state never
  repeat-log (no 5-second log spam).
- Testability: the HTTP message handler and the inter-attempt delay are
  injectable seams; tests use in-memory stubs and never sleep on wall-clock
  intervals and never open real sockets.
- Registered (and the hosted loop started) **only** in `ustreamer` mode.

### `CameraStatusOverlay : IDroneController` (Infrastructure decorator)

- Wraps the inner controller; `ConnectAsync`, `DisconnectAsync`,
  `SendCommandAsync`, `SetSpeedAsync`, and every status field except
  `State.Camera` pass through unchanged (verified by delegation tests).
- On `GetStatusAsync`, replaces `State.Camera` with `monitor.Current` — a
  cached, non-blocking read: **status reads never perform network I/O**, never
  await a probe, and never throw because of probe state.
- Registered composition-side only (applied by the internal helper in
  `Program.cs`'s safety factory), and only when `CAMERA_MODE=ustreamer`. In
  `mock` mode the class does not exist in the object graph.

## Behavior

### Mode semantics

| Environment | `CAMERA_MODE` | Observable camera behavior |
|---|---|---|
| Development PC (today) | unset or `mock` | Unchanged simulated behavior: `MockDroneController` derives `Connecting` during connect, `Streaming` when connected, `Offline` after disconnect/reset; hardware graphs keep honest `Offline` (`HardwareDroneController`). |
| Raspberry Pi with camera | `ustreamer` | Probe-backed status per the mapping table below; in hardware graphs the overlay replaces the controller's `Offline` with observed pipeline health. |
| Raspberry Pi without camera | `mock` | Honest `Offline` in hardware graphs (unchanged); no monitor, no probe. |
| Pi running the simulator with a real camera | `mock` + `ustreamer` | Allowed combination: the overlay replaces the simulator's camera field with observed pipeline health — an explicitly selected real camera never reports simulated `streaming`. |
| Any PC with `ustreamer` | `ustreamer` | Startup abort (platform gate) — no silent degradation. |

### Activation matrix

| `CAMERA_MODE` value | Startup result | Registered |
|---|---|---|
| Unset (null) | Startup succeeds | Nothing camera-related (exact today's registrations) |
| `mock` (explicit, case-insensitive, trimmed) | Startup succeeds | Identical registrations to "unset" (asserted by descriptor comparison) |
| Present but empty (`""`, whitespace-only) | **Aborted** — `HostAbortedException`: `CAMERA_MODE value '<raw>' is invalid. Accepted values: mock, ustreamer (case-insensitive).` | Nothing |
| Unknown value | **Aborted** — same message shape naming the key, the offending value, and the accepted values | Nothing |
| `ustreamer` on a platform that is not `linux/arm64` | **Aborted** — `HostAbortedException` mirroring the hardware gate: `CAMERA_MODE 'ustreamer' requires a Raspberry Pi runtime (linux/arm64); this process reports '<rid>'. Use 'mock' on a development PC.` (OS facts only; no device probing) | Nothing |
| `ustreamer`, platform OK, `Camera:ProbeUrl` missing or empty | **Aborted** — `HostAbortedException`: `CAMERA_MODE 'ustreamer' requires a Camera:ProbeUrl value (absolute http/https URL of the public camera path).` | Nothing |
| `ustreamer`, platform OK, `Camera:ProbeUrl` not an absolute `http(s)` URL | **Aborted** — `HostAbortedException` naming `Camera:ProbeUrl`, the offending value, and the expected shape | Nothing |
| `ustreamer`, platform OK, valid `Camera:ProbeUrl` | Startup succeeds | `CameraStatusMonitor` (singleton + hosted loop), `Camera:ProbeUrl` value; overlay applied in the safety factory |

- `mock` mode **never reads** the `Camera:` section: an invalid
  `Camera:ProbeUrl` present alongside `CAMERA_MODE=mock` (or unset) starts
  normally with mock registrations (mirrors how `mock` skips
  `MotorMapping`/`PwmMapping`).
- There is **no reachability gate at startup**: a structurally valid but
  unreachable `Camera:ProbeUrl` starts the host normally (D4 — the control
  plane must never depend on the camera). Health and `/api/drone/*` answer
  regardless; the camera status simply reports `Error` after the first failed
  attempt.
- The selected camera mode is logged once at startup (Information) alongside
  the existing hardware posture log — posture logging only, no claim of
  video.

### Status mapping (`CAMERA_MODE=ustreamer`)

| Condition | `State.Camera` |
|---|---|
| Safety path closed (`Unverified`/`StopPending`/`Faulted`/`Recovering`) | `Offline` — existing Task 26 override, composed above the overlay, wins over any probe value |
| First probe attempt not yet completed | `Connecting` |
| Last probe attempt succeeded (2xx within timeout) | `Streaming` |
| Last probe attempt failed (timeout, refusal, DNS, non-2xx) | `Error` |
| Pipeline recovers after failure | `Streaming` at the next successful attempt (automatic) |

Honesty label (normative): **`streaming` here means "the configured stream
endpoint answered 2xx to a headers-only GET" — it is endpoint reachability,
never verified picture, content, frame rate, or quality.** Physical video
verification belongs to Tasks 36/38 and stays `NOT VERIFIED`.

Pipeline health is independent of the drone session/connection (D4's plane
separation): the camera status reflects the video pipeline, not the
drone-connection field, and the safety override remains authoritative.

### Mock preservation

- `CAMERA_MODE` unset ≡ `CAMERA_MODE=mock`: identical service registrations
  (descriptor comparison) and identical simulated camera transitions; no
  monitor, no hosted loop, no overlay, no `Camera:` read.
- The frontend mock path (`VITE_DRONE_SERVICE=mock`) is untouched: zero
  frontend files change.
- Existing `MockDroneController`/`DroneSafetyController`/wire tests pass
  without weakened assertions.

## Stream Design (normative — implementation owners)

Everything in this section is binding design; the implementing task is listed
per item. Nothing here is coded by Task 31 beyond the two compose environment
lines.

```text
MEDIA PLANE (design fixed here)

Browser
  │  GET /camera/      relative, same-origin — never a hostname, never an IP,
  ↓                    no Vite/env variable (Task 33 consumes this constant)
Frontend nginx        existing host port: PC 8081 → container 80 (unchanged)
  │  location /camera/   (Task 32 implements; task 31 fixes the semantics)
  │    CAMERA_PROXY_TARGET unset/empty → 404   (nothing served; never SPA HTML)
  │    CAMERA_PROXY_TARGET set        → byte-for-byte proxy passthrough
  ↓  (compose-internal absolute URL; value owned by Task 32)
uStreamer             compose profile "camera" (name fixed here per the parent
  ↓                   reservation); internal port/endpoint OWNED BY TASK 32 —
Raspberry Pi Camera   never invented here; device access likewise (ledger)
```

1. **Stream URL strategy.** The browser always uses the relative, same-origin
   path `/camera/` — no Raspberry Pi IP, no hostname, no environment variable
   can reach React (backlog requirement). The backend probe uses the same
   path addressed from inside the compose network via `Camera:ProbeUrl`
   (value `http://frontend/camera/`, wired by Task 34).
2. **Stream path.** The parent-reserved `/camera/…` prefix; the browser
   request is `GET /camera/`. When the pipeline is disabled the location
   answers `404` — explicitly not the SPA fallback, so no client can mistake
   HTML for a stream. When enabled, nginx passes MJPEG bytes through
   untouched and never interprets frames.
3. **Port.** No new published host port anywhere: the browser reaches the
   stream on the existing frontend host port (`8081` on the PC convention;
   the Pi keeps its `.env`-defined convention from Tasks 20/34); nginx keeps
   listening on container port 80; the backend opens no listener. The uStreamer
   internal listen port is Task 32's decision (ledger row) and is supplied to
   nginx via configuration, never hardcoded in code by this task.
4. **Resolution / frame rate / image quality** — design defaults for Task 32's
   container configuration, operator-tunable via environment:
   **1280×720, 15 fps, JPEG quality 80.** These are encoder configuration
   choices, not hardware claims: whether the actual camera/Pi supports them is
   `NOT VERIFIED` (ledger; Task 36 verifies). No Task 31 code encodes them.
5. **Startup behavior.** Backend startup never waits for, requires, or probes
   the reachability of the camera pipeline; control services become healthy
   with the camera absent or down. The monitor starts its first probe
   immediately at host start (`Connecting` until then). Frontend nginx starts
   regardless of the camera profile; uStreamer starts under the `camera`
   profile (Task 32 attaches it; Task 34 wires `COMPOSE_PROFILES` in the Pi
   `.env`).
6. **Failure behavior.** Stream down or disabled → failures stay inside the
   nginx `/camera/` location (404 disabled; 502/504 upstream failure) and the
   monitor's `Error` status; `/api/` is never affected (D4), status reads
   never block, control containers are never restarted for camera failures,
   and probe failures log transitions rather than attempts. uStreamer
   container restart policy is Task 32's.
7. **Reconnect behavior.** MJPEG is stateless per request: the server keeps
   no per-client session, so reconnection is simply a fresh `GET` issued by
   the client (Task 33 owns retry/backoff UX; Task 32 owns container restart).
   The monitor recovers automatically at its next probe
   (`Error → Streaming`) with no latch and no operator action.

### Task boundaries

| Mechanism | Defined here | Implemented by |
|---|---|---|
| `CAMERA_MODE` parse, validation, platform gate | ✔ | **Task 31** |
| `Camera:ProbeUrl` contract + probe status mapping + overlay | ✔ | **Task 31** |
| Backend compose env passthroughs (`CAMERA_MODE`, `CAMERA__PROBEURL`) | ✔ | **Task 31** |
| Compose profile name `camera` (reserves the parent's "names fixed by Tasks 31/32") | ✔ | Task 32 (attaches service), Task 34 (`COMPOSE_PROFILES` wired) |
| nginx `location /camera/` + `CAMERA_PROXY_TARGET` semantics (empty → 404) | ✔ | Task 32 |
| uStreamer service, internal port/endpoint, device access, restart policy, applying the encoder defaults | ✔ values | Task 32 |
| Browser `streamUrl` constant, MJPEG rendering, client reconnect UX | ✔ strategy | Task 33 |
| `.env` values, `COMPOSE_PROFILES`, the concrete `Camera:ProbeUrl` deployment value | ✔ form + value | Task 34 (wired: `http://frontend/camera/`, `COMPOSE_PROFILES=camera`, `CAMERA_PROXY_TARGET=http://ustreamer:8080/stream`) |
| Pi-runtime verification of the whole pipeline; encoder-default support | ledger | Task 36 |
| End-to-end camera validation (real video in the browser) | ledger | Task 38 |

## Interfaces

- **Configuration**: `CAMERA_MODE` (env key, read via the composition root;
  no appsettings entry ships) and `Camera:ProbeUrl` (no default ships).
- **Compose**: backend service environment gains exactly
  `CAMERA_MODE: ${CAMERA_MODE:-mock}` and
  `CAMERA__PROBEURL: ${CAMERA_PROBE_URL:-}` (the `SAFETY__` double-underscore
  convention maps to `Camera:ProbeUrl`).
- **HTTP/wire**: unchanged — no new field, no new endpoint, no route, no
  camera health check; `camera` keeps its four camelCase string values.
- **Infrastructure seams**: `CameraStatusMonitor` (probe + cache),
  `CameraStatusOverlay` (decorator), internal
  `CameraRuntimeSelection.AddCameraRuntime` platform test seam
  (`InternalsVisibleTo`, mirroring `raspberryRuntimeSupported`).
- **DI registrations** (`ustreamer` only): `CameraStatusMonitor` as the
  singleton hosted loop plus the probe `HttpClient` (via `IHttpClientFactory`
  named client — no new package). `mock` registers nothing camera-related.
- **Domain/Application**: no additions; `IDroneController` is implemented,
  not changed.

## Validation

Whenever `CAMERA_MODE` is present:

1. Unset/null → `mock`. Present-but-empty (including whitespace-only after
   trim) → startup abort; unknown string → startup abort; both with the
   message naming the key, the raw value, and
   `Accepted values: mock, ustreamer (case-insensitive)`.
2. Accepted values are trimmed and case-insensitive (`mock`, `ustreamer`).
3. `ustreamer` requires the Raspberry runtime platform rule
   (`linux/arm64`, OS facts only, no device access) → otherwise startup abort
   naming `CAMERA_MODE`, the value, the requirement, the observed RID, and
   the `Use 'mock' on a development PC.` guidance (hardware-gate message
   shape).
4. In `ustreamer`: `Camera:ProbeUrl` must be present, non-empty, and an
   absolute `http://`/`https://` URL → otherwise startup abort naming the
   key. In `mock`: the `Camera:` section is never read (a present invalid
   value is ignored and registrations are identical to unset).
5. No startup-time reachability check of the probe URL exists (D4): only
   structural validation above.
6. Mapping correctness (monitor cache → overlay → wire) exactly as the
   mapping table, including `Connecting` before the first attempt, recovery,
   and safety-override precedence.
7. Probe semantics: headers-only GET, body never read, success = 2xx within
   the 3-second timeout, attempts every 5 seconds after the immediate first
   one, transitions-only logging, graceful cancellation at shutdown.
8. Scope of compose change: exactly two environment lines on the backend
   service; no service, port, profile, device, privilege, or healthcheck
   change; with an unset `.env` the rendered output equals today's behavior
   plus those two defaulted keys.

Example shape of startup failures (values are test fixtures, labeled as such
— never shipped):

```text
CAMERA_MODE value 'sometimes' is invalid. Accepted values: mock, ustreamer (case-insensitive).
CAMERA_MODE 'ustreamer' requires a Raspberry Pi runtime (linux/arm64); this process reports 'win-x64'. Use 'mock' on a development PC.
CAMERA_MODE 'ustreamer' requires a Camera:ProbeUrl value (absolute http/https URL of the public camera path).
Camera:ProbeUrl value 'frontend/camera' is invalid. Expected an absolute http:// or https:// URL.
```

## Error Cases

| Case | Observable behavior |
|---|---|
| `CAMERA_MODE` empty or unknown | `HostAbortedException` at startup naming key, value, accepted values; nothing registered; no fallback |
| `ustreamer` outside `linux/arm64` | `HostAbortedException` at startup (hardware-gate message shape); nothing registered |
| `ustreamer` with missing/empty/non-absolute-`http(s)` `Camera:ProbeUrl` | `HostAbortedException` at startup naming the key |
| `mock` with a `Camera:` section present (even invalid) | Ignored; startup succeeds; mock registrations identical to unset |
| Probe URL unreachable / timing out / refusing / DNS failure | Startup succeeds; probe failure caught inside the monitor (never propagates to requests); status → `Error`; control plane unaffected; transition logged once |
| Non-2xx response (including a 404 from a not-yet-implemented location) | Treated as failure → `Error` (honest: no reachable stream) |
| Status read while a probe is in flight or hung | Returns the cached value immediately (`Connecting` or last known); never blocks, never throws |
| Safety path closed | `Camera = Offline` forced by Task 26 above the overlay — wins over a `Streaming` monitor |
| Stream path down after Task 32 | nginx answers a proxy error (502/504) for `/camera/`; `/api/` untouched (defined here, implemented by Task 32) |
| `/camera/` requested in a `mock` deployment | `404`, never SPA HTML (defined here, implemented by Task 32) |
| Browser disconnects from an MJPEG request | No server-side session exists; backend not involved; client retry is Task 33 |
| Host shutdown mid-probe | In-flight attempt and delay cancelled promptly; cancellation not logged as an error |
| Any physical/video claim | Forbidden: `streaming` = endpoint answered 2xx to a headers-only GET; never "picture verified", never "video works" |

## Platform Requirements

- `net10.0`; Infrastructure + composition root only; **no new package
  references** (the `HttpClient` factory lives in the shared ASP.NET
  framework; the probe uses no library beyond `HttpClient`).
- No device paths, system calls, uStreamer types, or camera-driver types in
  any shipped code; the probe is plain outbound HTTP to a configuration value.
- Compose changes limited to two environment lines; no Dockerfile, nginx, or
  image-name changes in this task; the `linux/arm64` image build must remain
  green.
- Probe URL appears only as configuration; no camera hostname/IP literal is
  hardcoded in source (greppable rule).

## Security / Safety

- **Control/media separation (D4).** The backend never proxies, relays, or
  buffers video; the probe reads response headers only and disposes the
  response without touching the body; uStreamer never receives commands and
  never passes through ASP.NET; the media plane exists only in nginx (Task
  32).
- **No new inbound surface.** Task 31 adds no endpoint, route, or wire field;
  the route inventory and the five-endpoint contract are unchanged.
- **Fail-fast, no silent fallback (D5).** Invalid or empty `CAMERA_MODE`,
  a wrong platform, or a malformed required `Camera:ProbeUrl` abort startup
  loudly; nothing degrades from `ustreamer` to `mock`.
- **Control independence.** Camera unreachability never blocks startup,
  health, status reads, or commands, and never triggers control-container
  restarts; the probe runs outside the request path.
- **Honest status.** `Streaming` is reachability, not verified video (label
  above); `Error` is reported rather than simulated success; mock mode keeps
  its explicitly simulated semantics; the safety-closed `Offline` override
  still wins.
- **No Pi IP in React.** The browser-facing strategy is a relative same-origin
  path by design; nothing added here can put a host into the frontend bundle.
- **Minimal deployment delta.** Two defaulted environment lines only — no
  devices, no privileges, no new published ports, no profile changes.
- **Vocabulary isolation.** `CAMERA_MODE`, probe, and uStreamer vocabulary
  stays in Infrastructure/composition/deployment; Domain, Application, API
  controllers, and the frontend remain free of it.
- **Safety layer untouched.** Task 26 transitions, gates, and the Task 26
  decorator's outermost position are reused exactly.

## Verification Boundary

PC-verifiable in this task (claimed): mode parsing and its abort messages,
the platform gate (via the internal seam), the `Camera:ProbeUrl` structural
validation, the activation matrix (registrations identical for unset/`mock`),
probe semantics against stub HTTP endpoints (2xx/non-2xx/timeout/refusal/
recovery, headers-only, transition logging), overlay pass-through and
non-blocking status reads, safety-override precedence over a `Streaming`
monitor, control independence with an unreachable probe target, wire/route
stability, and the exact compose delta.

Explicitly **not** verified and not claimed: that any camera exists or
streams; that nginx `/camera/` proxies anything (location not implemented
until Task 32); that uStreamer builds, starts, or serves a well-formed MJPEG
endpoint; the internal port/endpoint layout; that the camera hardware
supports 1280×720/15 fps/quality 80; real video in a browser; behavior on a
Raspberry Pi. All remain prerequisites below.

## Prerequisites Ledger (camera rows)

| Physical/unknown fact | Owning task(s) | Status |
|---|---|---|
| Pi camera device presence, path, and access model | 32 (determine), 36 (verify) | `NOT VERIFIED` — `/dev/video*`-style paths must never be invented |
| uStreamer build, version, internal listen port, MJPEG endpoint layout | 32 (determined), 36 (verify) | Determined from upstream docs (Task 32): tag `v6.67`, default port `8080`, MJPEG endpoint `/stream`; Pi runtime behavior `NOT VERIFIED` |
| Support for the encoder defaults (1280×720, 15 fps, quality 80) | 32 (config), 36 (verify) | `NOT VERIFIED` — design defaults only, tunable |
| End-to-end video content, latency, quality on real hardware | 36, 38 | `NOT VERIFIED` |
| `CAMERA_MODE=ustreamer` stack behavior on a Raspberry Pi | 36 | `NOT VERIFIED` |
| MJPEG rendering/reconnect in the shipped browser | 33 (implemented, mock-tested), 38 (end-to-end verify) | Rendering, honest states, and bounded-backoff reconnect implemented (Task 33); real video `NOT VERIFIED` until Task 38 |

## Testing Scenarios

### Development-PC tests (`mock-tested` against stubs)

`DroneControl.Application.Tests`:

1. **`CameraStatusMonitorTests`** — initial `Current` is `Connecting`; a 2xx
   response (headers read, body never consumed — asserted by a body stream
   that fails on read) transitions to `Streaming`; 404/500 → `Error`;
   timeout → `Error`; connection refusal/DNS-style failure → `Error`;
   recovery `Error → Streaming` on the next success; repeated failures of
   the same state emit no repeated transition log (captured logger);
   shutdown cancels an in-flight attempt without error logging; deterministic
   runs via the injectable handler/delay seams (no wall-clock sleeps, no real
   sockets).
2. **`CameraStatusOverlayTests`** — only `State.Camera` is replaced (every
   other status field byte-identical); all operations delegate to an inner
   spy unchanged; the overlay reads only the monitor cache (a monitor whose
   probe never completes still yields an immediate status read); repeated
   reads never trigger network activity.

`DroneControl.Api.Tests` (new `CameraRuntimeCompositionTests`, mirroring
`MotorMappingCompositionTests`):

3. **Parse matrix** — unset → `mock`; `mock`/`ustreamer` accepted trimmed and
   case-insensitive; empty/whitespace-only and unknown values abort with the
   exact message shape (key, raw value, accepted values); no silent fallback.
4. **Platform gate** — `ustreamer` with the internal seam reporting "not
   supported" aborts with the hardware-gate message shape (key, value,
   requirement, observed RID, guidance); `ustreamer` with supported=true
   proceeds; `mock` proceeds regardless of platform.
5. **`Camera:ProbeUrl` rules** — missing, empty, relative, and non-`http(s)`
   values abort under `ustreamer` with messages naming the key; a valid
   absolute URL registers the monitor; `mock` with an invalid `Camera:ProbeUrl`
   present starts with registrations identical to unset (descriptor
   comparison), proving the section is never read.
6. **Registration matrix** — `ustreamer` valid config → monitor singleton +
   hosted loop + probe client registered; `mock` → zero camera registrations;
   the internal `ApplyCameraOverlay` helper returns the same instance in
   `mock` mode and an overlay in `ustreamer` mode.
7. **Safety precedence** — a composed graph `DroneSafetyController(
   ApplyCameraOverlay(inner, ustreamer, …))` on the real-mode seam with the
   D7 gate at `false` (existing faulted-at-start pattern): monitor reports
   `Streaming`, safety state is `Faulted`, and `GetStatusAsync` returns
   `camera: offline` — the Task 26 override wins.
8. **Control independence** — with the probe target refusing connections,
   status reads return `Error` promptly while connect/command/speed flow
   through safety + inner unchanged; no exception from the camera path ever
   surfaces on an API call.
9. **Shipped-config tests** — `appsettings.json` contains no `Camera:`
   section; `docker compose config` renders with the two new backend keys
   defaulting to `mock`/empty while services, ports, profiles, devices, and
   privileges are unchanged; D7 default remains `false`.
10. **Wire/route stability** — existing contract tests unchanged: four
    camelCase camera values, no new field, no new route.

### Regression

- All 334 existing backend tests (9 Domain + 246 Application + 79 Api) pass
  with **no assertion weakened or removed** (comment-only corrections are
  allowed and enumerated).
- Frontend suite (121) passes with zero frontend file changes (hash-verified).
- Two consecutive `dotnet test` runs are identical; build is 0 errors /
  0 warnings; `docker compose config` succeeds; the `linux/arm64` image build
  stays green.

### Physical confirmation (deferred — not a mandatory AC for Task 31)

uStreamer serving an actual MJPEG endpoint; the nginx `/camera/` proxy path
end-to-end; the encoder defaults on real camera hardware; real video rendered
in a browser; `CAMERA_MODE=ustreamer` on a Raspberry Pi. All `NOT VERIFIED`
at task completion (Tasks 32/36/38).

### Not proven by Task 31

That any stream exists or works; video content correctness; camera hardware
capabilities; browser-side reconnection; nginx location behavior; Pi runtime
behavior.

## Acceptance Criteria

- [x] `dotnet build backend/DroneControl.sln` succeeds with 0 errors and 0
      warnings, no package reference is added to any backend project, and no
      frontend file is modified (`npm test` in `frontend/` stays at 121
      passing tests).
- [x] `CAMERA_MODE` parsing: unset → `mock`; trimmed case-insensitive
      `mock`/`ustreamer` accepted; present-but-empty and unknown values abort
      startup via `HostAbortedException` naming `CAMERA_MODE`, the raw value,
      and `Accepted values: mock, ustreamer (case-insensitive)` — no silent
      fallback anywhere.
- [x] Platform gate: `ustreamer` on a platform that is not `linux/arm64`
      aborts startup with the hardware-gate message shape (key, value,
      requirement, observed RID, `Use 'mock' on a development PC.`), checked
      using OS facts only and exercised through the internal platform seam;
      `mock` starts on any platform.
- [x] `Camera:ProbeUrl`: under `ustreamer`, a missing, empty, or
      non-absolute-`http(s)` value aborts startup with a message naming the
      key; a valid absolute URL registers the monitor; under `mock` the
      `Camera:` section is never read and an invalid value still starts with
      registrations identical to an absent key.
- [x] No reachability gate: a structurally valid but unreachable
      `Camera:ProbeUrl` lets the host start; `/health` and `/api/drone/status`
      respond; the camera status reaches `error` after the first failed
      attempt; control commands are unaffected (D4).
- [x] Status mapping is exact: `connecting` before the first probe attempt;
      `streaming` only after a 2xx headers-only GET within the timeout;
      `error` on timeout/refusal/DNS failure/non-2xx; automatic recovery to
      `streaming`; safety-closed state forces `offline` over a `Streaming`
      monitor (composed test), with no change to Task 26 transitions.
- [x] Status reads never perform network I/O: with a probe that never
      completes, `GetStatusAsync` returns the cached value immediately and
      never throws; the probe reads response headers only (body never
      consumed), runs first-immediately-then-every-5s with a 3s timeout,
      logs transitions rather than attempts, and cancels cleanly at shutdown.
- [x] Overlay integrity: only `State.Camera` is remapped; every other status
      field and every `IDroneController` operation delegate unchanged to the
      inner controller; in `mock` mode the overlay, monitor, and hosted loop
      do not exist in the graph and simulated camera behavior (and service
      registrations) are identical with `CAMERA_MODE` unset, explicit
      `mock`, or with an invalid `Camera:` section present.
- [x] Honesty and isolation: no camera hostname/IP literal appears in
      backend source (probe URL comes only from configuration); `streaming`
      is documented and logged as endpoint reachability, never as verified
      video; `CAMERA_MODE`/probe/uStreamer vocabulary appears only in
      Infrastructure, composition root, and deployment files (greppable);
      Domain and Application gain no camera vocabulary.
- [x] Wire/API unchanged: `camera` keeps the four camelCase string values,
      no field/endpoint/route is added (existing contract tests pass
      unchanged), and no existing test assertion is weakened or removed —
      all 334 backend tests pass.
- [x] Compose delta is exactly two backend environment lines
      (`CAMERA_MODE: ${CAMERA_MODE:-mock}`, `CAMERA__PROBEURL:
      ${CAMERA_PROBE_URL:-}`); with an unset `.env`, `docker compose config`
      renders today's services, ports, profiles, devices, and privileges
      unchanged plus those two defaulted keys; the D7 gate default remains
      `false`.
- [x] Stream design is fixed and owned: this spec defines the relative
      `/camera/` browser path (no hostname/IP, no React env), the no-new-host-
      port rule, the encoder design defaults (1280×720 / 15 fps / quality 80,
      labeled `NOT VERIFIED` and never encoded in Task 31 code), the compose
      profile name `camera`, the `CAMERA_PROXY_TARGET` semantics (empty →
      `404`, never SPA HTML; set → passthrough), the `Camera:ProbeUrl` value
      form (`http://frontend/camera/` — existing service DNS + parent-
      reserved path), the startup/failure/reconnect behaviors, and a
      task-boundary table assigning implementation to Tasks 32/33/34/36/38.
- [x] Documentation: `docs/ARCHITECTURE.md` no longer claims `CAMERA_MODE`
      "remains design" (backend half implemented; transport half assigned to
      Tasks 32/34) and the `/camera/` line reflects "path defined, proxy
      implemented by Task 32"; `HardwareDroneController`/`Program.cs` camera
      comments are corrected; `MEMORY.md` gains the Task 31 entry; no
      physical claim is added anywhere.
- [x] Validation runs green twice: `dotnet restore`/`dotnet build`
      (0 errors, 0 warnings), `dotnet test` twice with identical results
      (334 existing + new tests, none removed or weakened), `npm test` (121),
      `docker compose config`, and the `linux/arm64` image build succeeds.
