# Frontend API Drone Service

## Purpose

Task 18 adds the real HTTP implementation of the existing frontend `DroneService` abstraction: `ApiDroneService`, which talks to the ASP.NET Core API (Tasks 10–16) and adapts the backend wire status to the richer frontend `DroneStatus` the UI already renders. The abstraction, the mock, and every UI component stay untouched — selection between implementations is environment-driven, with **mock remaining the default**. This task delivers the service and its tests; switching the dashboard over to real calls is Task 19.

Target architecture (this task creates the middle piece, not the flip):

```text
DroneService (interface, unchanged)
    ├── MockDroneService        (unchanged, still the default)
    └── ApiDroneService         (new)  →  ASP.NET Core API at VITE_API_BASE_URL
```

## Dependencies

- `frontend/src/services/droneService.ts` — the `DroneService` interface that must be preserved verbatim (`getStatus`, `connect`, `disconnect`, `sendCommand`, `setSpeed`) and the single module UI imports today.
- `frontend/src/types/drone.ts` — frontend `DroneStatus`/`CommandAck` shapes, including `services` grouping and `failedCommand` (both absent from the backend wire by design — MEMORY: "the frontend service abstraction adapts to its UI shape in Tasks 18/19").
- `specs/backend/drone-api.md` — the exact wire contract: endpoints, camelCase properties, lowercase string-only enum values, 400/409/415 behaviors.
- `specs/backend/error-handling.md` — 409 (command while disconnected), 503, generic 500 `application/problem+json` responses the adapter must surface as rejections.
- `specs/frontend/error-states.md` + `frontend/src/services/scenarioService.ts` — the deliberately mock-only failure-trigger contract.
- `frontend/src/utils/status.ts` — `normalizeDroneStatus` (single-point-of-exit convention to reuse).
- AGENTS.md / MEMORY.md safety rule: the frontend must never visually report physical command success without real hardware confirmation.
- Existing tooling: Vite, Vitest (fake `fetch` via `vi.fn` — no new dependencies), `tsc -b` build gate, oxlint.

## Scope

- `frontend/src/services/apiDroneService.ts`: `createApiDroneService(baseUrl, fetchImpl?)` implementing `DroneService` with the wire→UI mapping.
- Selection wiring in `frontend/src/services/droneService.ts` via `VITE_DRONE_SERVICE` (`'mock' | 'api'`, default `'mock'`) and `VITE_API_BASE_URL` (default `http://localhost:5080`).
- `src/vite-env.d.ts` (`/// <reference types="vite/client" />`) so `import.meta.env.VITE_*` type-checks under `tsc -b`, and `frontend/.env.example` documenting both variables.
- A scenario-service guard: with `'api'` selected, `scenarioService.runScenario` rejects with an explicit "unavailable in API mode" error; with `'mock'` (default) behavior is byte-identical to today.
- Vitest unit tests for the adapter (fake `fetch`) plus a throwaway live smoke test against the running backend.

## Out of Scope

- Any UI/hook change: `useDroneDashboard`, components, and existing tests stay untouched; the dashboard does not start using the API (Task 19 flips the default and integrates the flows).
- Polling/real-time transport (SignalR or status polling), request timeouts/retries/cancellation policy, and browser CORS end-to-end verification — all Task 19/20 concerns. (The backend Development CORS policy already allows `http://localhost:5173`.)
- Backend changes of any kind — the wire contract is consumed exactly as frozen by Tasks 12/16.
- Camera/uStreamer integration, authentication, error-message rendering, `failedCommand` population from a backend field (no such field exists).
- OpenAPI client codegen, HTTP libraries (axios etc.), state-management changes, Docker/Compose changes.
- Adding `.env` files to the repository (only `.env.example`; `.env` is gitignored).

## Architecture

```text
frontend/src/
├── services/
│   ├── droneService.ts        (modified: env-driven selection only)
│   ├── apiDroneService.ts     (new: the adapter)
│   └── scenarioService.ts     (unchanged)
├── types/drone.ts             (unchanged)
└── vite-env.d.ts              (new: vite/client type reference)
```

Rules:

- **Components never fetch**: `fetch` appears only inside `apiDroneService.ts`; the exported `droneService`/`scenarioService` const types stay `DroneService`/`ScenarioService`.
- **Single seam**: consumers keep importing `droneService` from `services/droneService.ts`; the selection module decides the implementation at import time from Vite env vars.
- **Testability**: the factory takes `(baseUrl, fetchImpl?)`, defaulting to global `fetch`, so unit tests inject a fake without global stubbing; the production call site passes only the env-derived URL.
- `createApiDroneService` returns an object satisfying `DroneService` exactly — TypeScript structurally enforces the abstraction is preserved.

## Behavior

### Wire → UI status mapping (applied to every response carrying a status)

Backend shape: `{ state: { connection, camera, requestedCommand, confirmedCommand, speed }, raspberryPi, api }`.

| Frontend field | Source | Rule |
| --- | --- | --- |
| `connection` | `state.connection` | Same enum values — pass through |
| `camera` | `state.camera` | Same enum values — pass through |
| `requestedCommand` / `confirmedCommand` | `state.*` | Pass through |
| `speed` | `state.speed` | Pass through |
| `services.drone` | `state.connection` | `connected → 'online'`; `offline/connecting/error` pass through |
| `services.raspberryPi` | `raspberryPi` | Same `connected → 'online'` mapping |
| `services.camera` | `state.camera` | `streaming → 'online'`; others pass through |
| `services.api` | `api` | Same `connected → 'online'` mapping (an HTTP 200 response always means the API is serving; the field is still mapped, not hardcoded) |
| `failedCommand` | — | Always `null`: the wire has no failed-command concept; command failures arrive as HTTP rejections, which the existing hook already handles by reloading status |

Every returned `DroneStatus` passes through `normalizeDroneStatus` before leaving the adapter (reuses the single-point-of-exit convention).

### Method semantics

| `DroneService` method | HTTP call | Notes |
| --- | --- | --- |
| `getStatus()` | `GET {base}/api/drone/status` | Maps response |
| `connect()` | `POST {base}/api/drone/connect` | Resolves after the backend's simulated ~700 ms connection (same UX pacing as the mock) |
| `disconnect()` | `POST {base}/api/drone/disconnect` | Maps the fresh offline status |
| `sendCommand(cmd)` | `POST {base}/api/drone/command` with JSON `{"command":"<cmd>"}` | Resolves the `CommandAck` below after the backend's ~250 ms simulated ack |
| `setSpeed(n)` | `PUT {base}/api/drone/speed` with JSON `{"speed":n}` | Values are expected pre-clamped by callers (hook does `clampSpeed`); out-of-range input simply rejects via backend 400 |

`baseUrl` handling: trailing slashes trimmed, paths joined as above — no double slashes; invalid URL join behavior is not a concern (documented default is a bare origin).

### Acknowledgement honesty (AGENTS safety rule)

`sendCommand` resolves `{ command, source: 'simulated', confirmedByHardware: false }`. That is the truthful description of today's backend (the endpoint is served by `MockDroneController`, and the wire carries no acknowledgement-source field). No code path in this task may ever produce `source: 'hardware'` or `confirmedByHardware: true`. A later hardware-phase specification must extend the wire contract to carry the real source — recorded as a known contract gap, not solved by guessing.

### Failures

- Non-2xx response → reject with an `Error` whose message names the method, path, HTTP status, and (when the body parses as ProblemDetails) its `title`/`detail`. Never resolves on error.
- Network failure / aborted fetch → the rejection propagates (no swallowing).
- Unparseable success body → reject (a malformed status is an error, not a silent default).
- Command while disconnected → backend 409 → rejection (the mock's silent resolve is deliberately **not** replicated; the existing hook's rejection path refetches status, which already handles this correctly).
- `'api'` mode + `runScenario(...)` → rejects with an explicit "mock scenarios are unavailable in API mode" error. In `'mock'` mode, everything behaves exactly as before (same shared instance, same scenario semantics).

### Environment configuration

| Variable | Values | Default | Meaning |
| --- | --- | --- | --- |
| `VITE_DRONE_SERVICE` | `'api'` \| `'mock'` | `'mock'` | Which implementation `droneService` exports (Task 19 will flip the default) |
| `VITE_API_BASE_URL` | any origin URL | `http://localhost:5080` | Backend base URL for `ApiDroneService` |

`frontend/.env.example` documents both with the defaults. No `.env` file is created.

## Validation

The adapter performs **no client-side re-validation** beyond what the abstraction already implies: speed is clamped by the existing hook (`clampSpeed`) and rejected by the backend (400); commands are enum-typed at the TypeScript boundary. The adapter's job is transport + mapping, not policy.

## Interfaces

Public surface (new):

```ts
createApiDroneService(
  baseUrl: string,
  fetchImpl?: typeof fetch,
): DroneService
```

Changed surface: none. `DroneService`, `ScenarioService`, `droneService`, `scenarioService` keep their exact current types; only the selected implementation and the api-mode scenario guard differ, both environment-driven.

## Platform Requirements

- Vite env handling for `VITE_`-prefixed vars (already available; `vite-env.d.ts` added so `import.meta.env` type-checks).
- Browser `fetch` (no polyfill needed for the project's targets) and Node ≥18 for tests/Vitest.
- Live smoke validation requires the backend running locally (`dotnet run` on 5080 or `docker compose up`); unit tests require no server.

## Security / Safety

- Simulated-ack honesty as specified above — the adapter can never imply hardware confirmation.
- No secrets in env examples or code; the API remains unauthenticated during development exactly as specified in the backend specs (auth is not introduced here).
- The adapter sends only the five documented endpoints; no GPIO/hardware concepts cross into the frontend (React never learns pin mappings).

## Testing Scenarios

Executed in order; the live harness is throwaway and deleted afterward:

1. Unit tests (`apiDroneService.test.ts`, fake `fetch` injected): mapping of a full connected wire status to the rich `DroneStatus` (services derivation incl. `streaming → online`, `failedCommand: null`); each method's HTTP verb/path/body; URL join with and without trailing slash; ack shape `{command, source:'simulated', confirmedByHardware:false}`; 409 → rejection mentioning 409; 500 ProblemDetails → rejection mentioning status and detail; network rejection propagates; malformed success body rejects.
2. Selection tests: default (no env) exports the mock — existing suites (App/hook/scenario/mock tests) still pass unchanged; `'api'` guard rejects `runScenario` (tested by directly invoking the exported behavior or an exported selection helper without breaking the module contract).
3. Typecheck + build: `npm run build` (`tsc -b && vite build`) succeeds; `npm run lint` (oxlint) clean for new files.
4. `npm test`: existing 99 tests plus the new adapter tests all pass (≥ 109 total, 0 failures).
5. Live smoke (temporary): start the real API on 5080; run a throwaway Vitest/node script calling `createApiDroneService('http://localhost:5080')`: connect → mapped `services.drone === 'online'` and `connection === 'connected'`; getStatus; `setSpeed(45)` → `speed: 45`; `sendCommand('forward')` resolves with the simulated ack; disconnect → mapped offline; `sendCommand('left')` after disconnect rejects with 409. Delete the temporary file and stop the API afterward.
6. Inventory: no changes to UI components, hooks, existing tests, mocks, types, `package.json`, backend, or Docker files.

## Acceptance Criteria

- [x] New files: `frontend/src/services/apiDroneService.ts`, `frontend/src/services/apiDroneService.test.ts`, `frontend/src/vite-env.d.ts`, `frontend/.env.example`; modified: `frontend/src/services/droneService.ts` (selection wiring only). No UI component, hook, type, mock, utility, existing test, backend, or Docker/Compose file is changed.
- [x] `frontend/package.json` is byte-identical: zero new dependencies; transport uses native `fetch` (injectable for tests).
- [x] The `DroneService` interface is unchanged and `createApiDroneService` returns a value typed as `DroneService`; UI consumers still import only from `services/droneService.ts` (grep-proven: no component/hook imports `apiDroneService`).
- [x] `fetch(` appears nowhere in `frontend/src` outside `apiDroneService.ts`.
- [x] Backend URL is configured via `VITE_API_BASE_URL` with documented default `http://localhost:5080`, trailing-slash safe; service selection via `VITE_DRONE_SERVICE` defaulting to `'mock'`; both variables documented in `frontend/.env.example`; no `.env` file is added.
- [x] The five methods hit the frozen contract exactly: `GET /api/drone/status`, `POST /api/drone/connect`, `POST /api/drone/disconnect`, `POST /api/drone/command` (body `{"command":"<cmd>"}`), `PUT /api/drone/speed` (body `{"speed":<n>}`), all via unit-test assertions on the injected fake fetch.
- [x] A connected wire status maps to the frontend `DroneStatus` with: passthrough of connection/camera/requested/confirmed/speed; `services.drone/raspberryPi/api` derived via `connected → 'online'`; `services.camera` derived via `streaming → 'online'`; `failedCommand` always `null`; result normalized through `normalizeDroneStatus`.
- [x] `sendCommand` resolves `{ command, source: 'simulated', confirmedByHardware: false }`, and no code path can emit a hardware acknowledgement (source inspection + test).
- [x] Non-2xx responses reject with an error message containing the HTTP status (verified for a 409 and a 500 with ProblemDetails body), network failures propagate as rejections, and an unparseable 200 body rejects — the adapter never resolves on error and never throws synchronously from a resolved promise chain.
- [x] With `'mock'` selection (default), `droneService` and `scenarioService` are the same mock instance as today and all 99 existing frontend tests pass unmodified; with `'api'` selection, `scenarioService.runScenario` rejects with an explicit unavailable-in-API-mode error.
- [x] `npm test` passes with 0 failures including at least 10 new adapter tests; `npm run build` succeeds; `npm run lint` reports no problems for the new files.
- [x] The live smoke test against the running backend passes end-to-end (connect mapping, speed set, ack shape, offline command 409 rejection), and the temporary harness file is deleted and the API stopped afterward.
- [x] The dashboard's visible behavior is unchanged by this task (mock remains the default implementation — verified by existing App/hook tests passing untouched).
