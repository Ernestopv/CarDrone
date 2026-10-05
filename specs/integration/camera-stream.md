# Camera Stream Integration (Task 33)

## Purpose

Connect the existing `CameraView` to the real uStreamer MJPEG feed served at
the reserved same-origin path `/camera/` (Tasks 31/32), while preserving the
simulated camera experience on the PC. This task is the frontend (browser)
half of the media plane: rendering the stream, honest status presentation,
and client-side reconnect. It adds no backend code and no new published host
port.

## Dependencies

- `specs/hardware/camera-runtime.md` (Task 31) — normative design this task
  consumes: the browser always uses the relative same-origin constant
  `/camera/` (no hostname, no IP, no build-time React/env variable); MJPEG is
  stateless per request, so reconnection is a fresh `GET` (client retry/backoff
  UX is owned here); `streaming` means endpoint reachability, never verified
  video; mock mode serves no `/camera/` traffic.
- `specs/docker/ustreamer-container.md` (Task 32) — the transport this task
  consumes: nginx `/camera/` location (`CAMERA_PROXY_TARGET` empty → `404`,
  set → byte passthrough to uStreamer), the `ustreamer` service, and the
  frontend env line `CAMERA_PROXY_TARGET`.
- `specs/integration/frontend-backend.md` + `specs/integration/frontend-api-service.md`
  — the `DroneService` abstraction, the service-selection seam, and the
  architectural rule that `fetch` exists only inside service implementations.
- `specs/frontend/camera.md` + `specs/frontend/error-states.md` — the existing
  `CameraView` (mock camera states, the unused `streamUrl` prop reserved for
  this task) and the UI failure semantics.
- `docs/DECISIONS.md` D4 — control plane (`/api/`) and media plane (`/camera/`)
  never merge; camera *status* still arrives via `DroneStatus.camera`.
- Existing frontend implementation: `src/features/camera/CameraView.tsx`,
  `src/services/{droneService,apiDroneService,mockDroneService}.ts`,
  `src/types/drone.ts`, `src/App.tsx` (the `CameraView` mount point).

## Scope

- Frontend: a runtime `CameraMode` concept and a `DroneService.getCameraMode()`
  capability backed by a small, same-origin, nginx-served document; the
  `CameraView` real-feed rendering (MJPEG `<img>`), honest state overlays, and
  client reconnect with bounded backoff; feature tests; the App wiring.
- Frontend deployment: a second entry-point script that renders the
  `camera-mode.json` capability document from the existing
  `CAMERA_PROXY_TARGET` gate (Task 32's `50-camera-location.sh` untouched).
- Removing the now-serviceable `streamUrl` prop in favor of the fixed
  `/camera/` constant.
- Documentation/ledger updates (MEMORY, ARCHITECTURE, camera-runtime ledger row).

## Out of Scope

- Any backend change (`/api/` wire, routes, `DroneStatus` shape) — frozen; the
  capability document is served by the frontend nginx, not the backend.
- uStreamer service / nginx passthrough behavior (Task 32, COMPLETED).
- `.env`/`COMPOSE_PROFILES` final wiring (Task 34).
- Pi-runtime verification and encoder-default support (Task 36); end-to-end
  real-video-in-browser validation (Task 38).
- Camera device path/access (Task 24 inventory / Task 36).

## Architecture

```text
Browser (CameraView)
   │  mode = DroneService.getCameraMode()   ← same-origin GET /camera-mode.json
   │        (mock | ustreamer)                (rendered by the frontend nginx
   │                                           entry point from CAMERA_PROXY_TARGET)
   ├── mock mode       → today's simulated placeholder (PC unchanged)
   └── ustreamer mode  → <img src="/camera/"> MJPEG feed (constant, same-origin)
            │           LIVE FEED badge only after the first decoded frame
            │           STREAM ERROR + bounded-backoff retry (fresh GET) while
            │           status stays streaming; honest overlays otherwise
            ↓
        nginx /camera/  → (Task 32) → uStreamer
```

The frontend never learns a Pi hostname/IP, never bakes a build-time
environment flag into which deployment mode it renders, and never calls
`fetch` outside the service layer. The mode discriminator is a runtime
capability document served by the same nginx that serves the app — the only
honest, deterministic signal that works with the frozen backend wire and the
identical image on both platforms.

### Why a runtime capability document (decision)

`DroneStatus.camera` uses the same four values in both modes (simulated in
`mock`, probe-driven in `ustreamer`), and the shipped frontend image must be
identical on PC and Pi — so neither the wire nor a build-time env can
distinguish the modes. The nginx entry point already knows the media plane's
state through `CAMERA_PROXY_TARGET`; recording it once into a tiny
same-origin JSON is deterministic and testable (a pure stream-reachability
probe was rejected: it cannot tell a PC `404` from a Pi `502` without the very
discriminator it is trying to infer).

## Domain Model

Frontend-only additions (no wire field, backend untouched):

```ts
export type CameraMode = 'mock' | 'ustreamer'
```

`DroneService` gains:

```ts
getCameraMode(): Promise<CameraMode>
```

## Behavior

- **Mode resolution (once at app start).** The app calls
  `droneService.getCameraMode()` and passes the result to `CameraView`.
  - `ApiDroneService`: same-origin `GET /camera-mode.json`;
    body `{"mode":"ustreamer"}` → `ustreamer`; a `404` (vite dev, PC without
    the file) → `mock`; any other failure → rejects (the app then shows an
    honest camera-unavailable state — never a simulated one).
  - `MockDroneService`: resolves `mock`.
- **Mock mode.** `CameraView` renders exactly today's UI (simulated
  placeholder and labels; no `<img>`); existing behavior and tests preserved.
- **Ustreamer mode, status `streaming`.** Render `<img src="/camera/">`
  (module constant `CAMERA_STREAM_PATH = '/camera/'`). Before the first frame
  decodes, overlay `CONNECTING...`; after the image fires `load` (first frame
  rendered), overlay `LIVE FEED` — the only point at which live video is
  claimed. Never render the simulated placeholder or "SIMULATED STREAM" in
  this mode.
- **Ustreamer mode, other statuses.** `offline` → `NO SIGNAL`; `connecting` →
  `CONNECTING...`; `error` → `STREAM ERROR`; no `<img>`, no simulated text.
- **Reconnect (fresh GET).** If the `<img>` errors while status is
  `streaming`, show `STREAM ERROR` (grace) and retry by remounting the
  `<img>` (a fresh `GET`, per Task 31) with backoff `1s → 2s → 4s` capped at
  5s, continuing while status stays `streaming`. Retries stop when status
  departs `streaming`, on success, or on unmount; timers are cleaned up. A
  status transition back to `streaming` always resets to a fresh `GET`.
- **Honesty.** `LIVE FEED` appears only after an actual first frame; the
  simulated labels are unreachable in `ustreamer`; no host/IP/env values
  exist in component code; a failed/degraded stream never impersonates a
  live one.

## Interfaces

**Frontend code**

```text
src/types/drone.ts              + CameraMode = 'mock' | 'ustreamer'
src/services/droneService.ts    + getCameraMode() on DroneService
src/services/apiDroneService.ts + getCameraMode() (same-origin /camera-mode.json;
                                fetch confined to this module)
src/mocks/mockDroneService.ts   + getCameraMode() → { mode: 'mock' }
src/features/camera/CameraView.tsx  + cameraMode prop; real-feed branch;
                                remove unused streamUrl prop
src/features/camera/useCameraStream.ts  (new) feed/retry hook
src/App.tsx                     resolve cameraMode once; pass it down
src/features/camera/CameraView.test.tsx + hook tests
```

**Frontend deployment**

```text
frontend/docker-entrypoint.d/51-camera-mode.sh  (new)
  writes /usr/share/nginx/html/camera-mode.json:
    CAMERA_PROXY_TARGET set   → {"mode":"ustreamer"}
    empty/unset               → {"mode":"mock"}
```

No `docker-compose.yml` change is required (the existing
`CAMERA_PROXY_TARGET` env already reaches the frontend service). Task 32's
`50-camera-location.sh` and its verified behavior are untouched.

## Validation

- `npm run build` (TypeScript) 0 errors; `npm test` green: existing 121 tests
  pass (only intended `CameraView`/`App`/test-helper edits) plus new camera
  tests.
- Backend untouched: `dotnet build`/`dotnet test` unchanged (regression).
- Frontend-image runtime check on the PC (no camera): with
  `CAMERA_PROXY_TARGET` empty, the image serves `GET /camera-mode.json` as
  `{"mode":"mock"}` and `/camera/` as `404`; with the variable set to a stub
  upstream, it serves `{"mode":"ustreamer"}` and `/camera/` passes bytes
  through — proving the Task 32 behavior is preserved.
- `docker compose config` unchanged (no compose edit in this task).

## Error Cases

| Case | Observable behavior |
| --- | --- |
| `getCameraMode()` returns `404` | Treated as `mock` (dev PC / mock deployment) |
| `getCameraMode()` network/other failure | Mode resolution fails; app shows honest camera-unavailable, never simulated UI |
| `<img>` error while status `streaming` (real mode) | `STREAM ERROR` overlay + bounded-backoff retry (fresh GET); stops when status changes or unmount |
| Status leaves `streaming` | Feed `<img>` removed; status overlay shown; timers cleared |
| Stream returns after failure (status → `streaming`) | Fresh `GET`; `LIVE FEED` only after a first frame renders |
| Status `streaming` but image never decodes (misconfigured upstream) | Grace error + retry; never "SIMULATED STREAM"; no permanent live claim |
| Mock mode on PC | No `<img>` ever mounted; simulated behavior identical to today |

## Platform Requirements

- Browser: native MJPEG (`multipart/x-mixed-replace`) `<img>` decoding in all
  supported browsers — standard behavior; confirmed in implementation tests
  (jsdom stubs dispatch `load`/`error` explicitly).
- No new npm dependency, no backend change, no new host port, no secrets.
- Same frontend image on PC and Pi; deployment difference is configuration
  (`CAMERA_PROXY_TARGET`) only.

## Security / Safety

- Fetch remains confined to service implementations (existing rule); the
  component gets data via props only.
- No simulated stream may be presented as real, and no real stream may be
  claimed before a first frame renders (honesty boundary, AGENTS).
- The capability document contains no secret, no device path, no IP.
- Control-plane independence (D4): stream failures affect only the camera
  panel's presentation, never commands or status.

## Testing Scenarios

1. `getCameraMode`: API service with a stubbed fetch — 200
   `{"mode":"ustreamer"}` → `ustreamer`; 404 → `mock`; network error →
   rejects. Mock service → `mock`.
2. `CameraView` mock mode: today's simulated states and labels (existing
   assertions preserved).
3. `CameraView` ustreamer + `streaming`: `<img src="/camera/">` mounted; no
   simulated text; `CONNECTING...` before `load`; `LIVE FEED` after a fired
   `load`.
4. `CameraView` ustreamer + `offline`/`connecting`/`error`: honest overlays,
   no `<img>`.
5. Reconnect: fired `error` on the `<img>` while `streaming` → retry remount
   scheduled with fake timers (backoff sequence), stops on status change and
   on unmount; `streaming` after `error` issues a fresh `GET`.
6. App wiring: `cameraMode` resolved once from the selected service and passed
   to `CameraView` (mock vs api variants).
7. Accessibility: the status text keeps `role="status"`; the live badge is
   announced.
8. Regression: whole frontend suite (existing + new) and untouched backend.

## Acceptance Criteria

- [x] `types/drone.ts` gains `CameraMode = 'mock' | 'ustreamer'`; `DroneService`
      gains `getCameraMode(): Promise<CameraMode>`; `MockDroneService` returns
      `mock`; `ApiDroneService` resolves it from the same-origin
      `/camera-mode.json` (200 `{"mode":"ustreamer"}` → `ustreamer`;
      `404` → `mock`; other failure → rejects). All `fetch` calls remain
      inside service implementations.
- [x] The frontend image gains `docker-entrypoint.d/51-camera-mode.sh` which
      writes `/usr/share/nginx/html/camera-mode.json` (`{"mode":"ustreamer"}`
      when `CAMERA_PROXY_TARGET` is set, `{"mode":"mock"}` otherwise); Task
      32's `50-camera-location.sh` is unchanged; no `docker-compose.yml`
      change exists in this task's diff.
- [x] `CameraView` in `mock` mode renders exactly today's UI and its existing
      tests pass unchanged.
- [x] `CameraView` in `ustreamer` mode with status `streaming` mounts
      `<img src="/camera/">` (module constant, same-origin, no host/IP/env);
      `LIVE FEED` is shown only after the first frame (`load`); the simulated
      placeholder and "SIMULATED STREAM" are unreachable in this mode.
- [x] `CameraView` in `ustreamer` mode with statuses `offline`/`connecting`/
      `error` shows honest overlays (`NO SIGNAL`/`CONNECTING...`/`STREAM
      ERROR`) and no `<img>`.
- [x] Reconnect: an `<img>` error while status is `streaming` schedules a
      fresh-`GET` retry with bounded backoff (1s→2s→4s, cap 5s) that stops on
      status change or unmount; returning to `streaming` always resets to a
      fresh `GET`; timers are cleaned up.
- [x] The unused `streamUrl` prop is removed; the `/camera/` constant is the
      single stream source.
- [x] `App` resolves `cameraMode` once at startup from the selected service and
      passes it to `CameraView`; resolution failure surfaces an honest
      camera-unavailable state (never simulated).
- [x] New and updated tests cover: service mode resolution (200/404/error +
      mock), the `CameraView` mock and ustreamer state matrix, retry/backoff
      (fake timers), a11y `role="status"`, and App wiring. The existing 121
      frontend tests pass (only intended edits); `npm run build` 0 errors.
- [x] No backend file or compose change: `dotnet build`/`dotnet test` and
      `docker compose config` unchanged.
- [x] Frontend-image runtime check (PC, no camera): empty `CAMERA_PROXY_TARGET`
      serves `camera-mode.json {"mode":"mock"}` and keeps `/camera/` as `404`;
      a set target serves `{"mode":"ustreamer"}` and keeps byte passthrough.
- [x] Documentation/ledger: `MEMORY.md` gains the Task 33 entry;
      `docs/ARCHITECTURE.md` camera line reflects the browser feed wiring
      (real video still `NOT VERIFIED` — Task 38); the camera-runtime ledger
      row for browser rendering/reconnect is updated; no physical claim is
      added anywhere.

## Backlog Status

Implemented and validated on software-level evidence (Task 33 `COMPLETED`):
frontend build 0 errors, oxlint clean, 138 tests green, and the built
frontend image serves the mode capability document and preserves the
`/camera/` 404/passthrough behavior. Real MJPEG in a browser and Pi-runtime
behavior remain `NOT VERIFIED` per the ledger (Tasks 36/38).