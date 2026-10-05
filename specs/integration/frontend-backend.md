# Connect Frontend to Backend

## Purpose

Task 19 flips the dashboard from simulated data to the real backend, completing the Phase 4 integration chain:

```text
React
 ↓
ApiDroneService      (built in Task 18)
 ↓
ASP.NET Core API     (built in Tasks 12/16)
 ↓
MockDroneController  (built in Task 11 — still the drone-side implementation; no Raspberry Pi yet)
```

The UI layer is not rewritten: the existing components and the `DroneService` abstraction stay as they are. Task 19 makes the API-backed implementation the **default selection**, makes the initial dashboard state reflect the **actual backend state** (a status load on mount), handles backend failures through the already-specified error paths, and gates the mock-only scenario panel so it can never masquerade as working when a real backend is driving the UI.

## Dependencies

- `specs/integration/frontend-api-service.md` (Task 18) — `ApiDroneService`, the `VITE_DRONE_SERVICE`/`VITE_API_BASE_URL` selection seam, the api-mode `scenarioService` rejection guard, and `vite-env.d.ts`. This task changes only the default of that seam and consumes it.
- `frontend/src/hooks/useDroneDashboard.ts` — all dashboard behavior lives here; its optimistic-request/confirmed-ack split, `reportServiceFailure` error routing, and rejection-refetch paths are the failure-handling machinery this task reuses (requirements: "Preserve UI behavior", "Keep command acknowledgement semantics", "Handle backend failures correctly").
- `frontend/src/App.tsx` + `features/connection/MockScenariosPanel.tsx` — the panel renders mock failure-injection triggers; in API mode those triggers have no meaning (Task 18 guard rejects them), so App must stop offering them.
- `specs/backend/drone-api.md` — Development CORS from `http://localhost:5173` already exists (Task 12 config), and the Compose stack (Task 14) runs the API in Development, so the browser path is already served; nothing backend-side changes.
- `specs/frontend/error-states.md` — the error-state semantics the existing hook routes failures into; unchanged.
- `frontend/src/test/setup.ts` + `vitest.config.ts` — the test harness this task pins to mock mode so the existing ~114-test suite keeps its meaning after the default flip.
- AGENTS/MEMORY safety rules: the UI never claims hardware confirmation (ack stays simulated all the way down — the backend's drone-side implementation is still `MockDroneController`), and no GPIO/hardware concept may cross into the frontend.

## Scope

- Default flip: unset `VITE_DRONE_SERVICE` → **`api`** (`mock` becomes the explicit opt-out); update `frontend/.env.example` accordingly.
- Capability flag: `droneService.ts` additionally exports `supportsMockScenarios: boolean` (true only when the mock is selected).
- App gating: `MockScenariosPanel` renders only when `supportsMockScenarios` (hidden with API mode; the Task 18 rejection guard remains as defense-in-depth, and the hook's `runScenario` keeps its contract).
- Status integration at startup: `useDroneDashboard` loads the real status via `droneService.getStatus()` on mount; success replaces the offline placeholder, failure routes to the existing `reportServiceFailure` path.
- Test setup pinning (`setup.ts` forces mock mode so existing App/hook/adapter suites remain mock-semantics suites), plus new tests for: mount-load success/failure, API-mode panel absence, mock-mode panel presence, and an api-mode end-to-end hook flow with a stubbed fetch.
- Live throwaway end-to-end validation against the running backend, including the Development CORS preflight.

## Out of Scope

- Any backend, Docker, or Compose change — the API, its CORS policy, and the images are consumed exactly as frozen.
- Polling, WebSockets/SignalR, or any push status updates: the single startup load + existing flow-driven refreshes are the integration level required here (real-time is a later phase).
- Retries, request timeouts, abort/cancellation policies.
- Authentication, HTTPS, deployment.
- Camera/uStreamer integration, Task 20's full-stack Compose (frontend container), hardware/Phase-3 anything.
- UI redesign or new error displays: existing components, labels, and alerting stay untouched.
- Populating `failedCommand` from a backend field (none exists; Task 18 mapping keeps it `null`).
- Removing or disabling the mock — it stays selectable for backend-less UI development.

## Architecture

```text
frontend/src/
├── services/droneService.ts   (modified: default 'api' + supportsMockScenarios export)
├── hooks/useDroneDashboard.ts (modified: mount status load + scenario guard uses the flag)
├── App.tsx                    (modified: conditional MockScenariosPanel)
├── test/setup.ts              (modified: pin VITE_DRONE_SERVICE='mock' for tests)
└── .env.example               (modified: document the new default + mock opt-out)
```

Decisions embedded in the design:

1. **The flip is the default, not a deletion.** Mock remains reachable with one env variable — satisfying Task 18's "keep mock available when useful" while Task 19's goal (real calls replace simulated ones) holds by default.
2. **Capability flag instead of implementation branching in data paths.** `supportsMockScenarios` gates a *mock-only developer tool* (failure injection). Telemetry/state continues to flow exclusively through `DroneService`; no component branches on how status data is produced. The alternative — leaving the panel wired to a rejecting service — would let a dev button drive the whole UI into "error" for a non-event, which is exactly the dishonest behavior the safety rules forbid.
3. **Mount load makes the UI truthful against a persistent backend.** The API's simulator is a singleton that survives browser reloads (`docker compose up` keeps state); without a startup load the UI would claim "offline" while the drone-side model is connected. On failure the UI shows the existing error state rather than an optimistic offline guess.
4. **Test environment pins mock.** Without the pin, flipping the default would silently turn all existing UI suites into network tests. The pin keeps them behavior tests of the abstraction, and new explicitly-stubbed tests cover the API-mode surface.

## Behavior

### Selection and startup

| Situation | Result |
| --- | --- |
| `VITE_DRONE_SERVICE` unset | ApiDroneService + `http://localhost:5080` (or `VITE_API_BASE_URL`) |
| `VITE_DRONE_SERVICE=mock` | today's mock behavior end-to-end, including the scenarios panel |
| `VITE_DRONE_SERVICE=api` | identical to unset |
| Test runs | `setup.ts` pins `mock`; API-mode tests override via `vi.stubEnv` and restore |
| Mount, service reachable | `getStatus()` result replaces the initial placeholder status |
| Mount, service unreachable | existing `reportServiceFailure` path: error state, console log |

### Interactive flows (unchanged semantics, now against the real API)

- **Connect/disconnect**: pending UI + local "connecting" status while `POST connect` (≈700 ms backend pacing) resolves with the mapped status — the same UX contract the mock provided, now served by ASP.NET Core + `MockDroneController`.
- **Commands (buttons + keyboard)**: optimistic `requestedCommand`; `confirmedCommand` updates **only** after the service call resolves (ack semantics preserved); the ack remains `{ source: 'simulated', confirmedByHardware: false }` because the backend's drone side is still simulated — nothing in this task can or may claim hardware confirmation.
- **Command rejected by backend** (e.g., 409 after a state race): existing rejection path logs and reloads the true status from the backend — the UI converges on reality instead of faking.
- **Speed**: UI clamp stays client convenience; the backend stays authoritative (400 → existing failure path).

### Failure handling routing (all pre-existing paths — this task adds no new mechanism)

| Backend condition | UI result |
| --- | --- |
| Unreachable at mount | error status (drone/camera error) via `reportServiceFailure` |
| connect/disconnect network failure | error status via existing catch |
| 400/409/500/503 on a command | rejection → status reload → truthful state |
| Command rejected *and* reload fails | error status (existing nested catch) |
| speed PUT failure | error status via existing `.catch` |
| `runScenario` called in API mode (API-level misuse) | still rejects (Task 18 guard); hook ignores silently via the `supportsMockScenarios` guard so it can never reach the UI |

### Scenario panel

`App.tsx` renders `<MockScenariosPanel>` only when `supportsMockScenarios` is true. Hidden state, not disabled-and-erroring: in API mode the triggers are meaningless. The panel component, its props, and its tests are untouched; mock-mode App tests therefore keep passing unchanged.

## Interfaces

Added public surface (one export):

```ts
// frontend/src/services/droneService.ts
export const supportsMockScenarios: boolean
```

Unchanged: the `DroneService` interface, `droneService`/`scenarioService` types, `UseDroneDashboardResult`, and every component prop.

## Validation

No new validation policy: client-side speed clamping stays in the hook (UI convenience), backend 400s stay authoritative, and enum typing at the service boundary is unchanged.

## Platform Requirements

- Vite dev server default origin `http://localhost:5173` — already in the backend's Development `Cors:AllowedOrigins`.
- Live validation runs the API on 5080 (`dotnet run --no-build --urls http://localhost:5080` or `docker compose up`) alongside the checks below; no browser automation tooling is required or added.

## Security / Safety

- Ack honesty preserved by construction: the deepest drone-side implementation remains the simulator; `source: 'simulated'` cannot be bypassed by any Task 19 change, and no new code path may produce a hardware claim.
- The mount-load failure path never fabricates success: unreachable backend renders as error, not as "offline and idle".
- No GPIO/pin/device concepts appear anywhere (flag and flows are about *data source capability*, not hardware).
- No secrets, no new endpoints, no backend auth changes; CORS behavior comes solely from the existing Development configuration.

## Testing Scenarios

Executed in order; throwaway harnesses deleted afterward:

1. Unit (jsdom, stubbed env/fetch — no network):
   - mount-load success: api-selected hook + stubbed global `fetch` returning a connected wire status → UI status becomes connected with `services.*` online;
   - mount-load failure: fetch rejects → error state + console error;
   - panel gating: api selection → `MockScenariosPanel` absent from rendered `App`; mock selection → present (existing App tests remain as the mock-mode proof);
   - ack semantics unchanged: command confirmed only after the service promise resolves (existing hook tests + a new api-mode equivalent);
   - `supportsMockScenarios` true only under mock selection.
2. `npm test` — all previous suites (setup-pinned mock) plus the new API-mode tests pass, 0 failures; `npm run build`; `npm run lint`.
3. Live E2E (temporary, real backend on 5080, api selection via stubbed env; deleted afterward): render the hook with real `fetch`; connect → (after ≥ ~700 ms pacing) connected/online mapping; send `forward` → confirmed after backend ack; `setSpeed(45)`; disconnect → offline; stop the API and assert the mount-load failure path renders error (restart-free variant: fetch rejection test already covers — live check optional).
4. CORS preflight (real backend, Development): `OPTIONS /api/drone/command` with `Origin: http://localhost:5173` + preflight headers → 2xx with `Access-Control-Allow-Origin: http://localhost:5173`; `GET /api/health` with the same origin → the header present (simple-request path).
5. Inventory/regression: only the five planned modified files (+ new/updated test files); `package.json` byte-identical; backend untouched (hash diff + `dotnet build` 0/0 + `dotnet test` 51/51).

## Acceptance Criteria

- [x] Modified files are exactly `frontend/src/services/droneService.ts`, `frontend/src/hooks/useDroneDashboard.ts`, `frontend/src/App.tsx`, `frontend/src/test/setup.ts`, and `frontend/.env.example` (plus new/updated test files under `frontend/src`); no backend, Docker, Compose, or `package.json` file changes (byte-identical hash for the latter two categories verified).
- [x] With `VITE_DRONE_SERVICE` unset, the app selects `ApiDroneService` against `VITE_API_BASE_URL` (default `http://localhost:5080`); `VITE_DRONE_SERVICE=mock` reproduces today's exact behavior including the scenarios panel; `.env.example` documents the new default and the mock opt-out without introducing any committed `.env` file.
- [x] `droneService.ts` exports `supportsMockScenarios`, true only in mock selection; `App.tsx` renders `MockScenariosPanel` only when it is true; in api mode the panel is absent from the rendered App (new test) while in mock mode all existing App/scenario tests pass unchanged.
- [x] `useDroneDashboard` loads status on mount through `droneService.getStatus()`: a successful backend status replaces the initial placeholder (proven with stubbed fetch returning a connected wire payload), and a failed load routes through `reportServiceFailure` producing the existing error state (proven with a rejecting fetch); mock-mode mount behavior is unchanged (existing suites pass).
- [x] Command acknowledgement semantics are preserved against the API path: `confirmedCommand` updates only after the service promise resolves; the resolved ack is `{ source: 'simulated', confirmedByHardware: false }`; no added or modified code path can surface a hardware confirmation (source inspection of all Task 19 diffs).
- [x] Backend failures reuse existing paths only: 409 command rejection triggers the status reload convergence behavior; connect/disconnect/speed network failures surface as the specified error state; no new error-handling mechanism, component, or state is introduced.
- [x] The test setup pins mock mode so every pre-existing frontend test file runs with identical semantics (zero modifications to existing test assertions), and the new API-mode tests use explicit env stubbing/stubbed fetch with proper restoration.
- [x] `npm test` passes with 0 failures and at least 4 new tests (mount-success, mount-failure, panel-absent-in-api-mode, `supportsMockScenarios` truth table); `npm run build` and `npm run lint` succeed.
- [x] The live end-to-end check drives the real stack (browser-shaped fetch against the running API): connect → mapped connected/online state through the hook, command → confirmed, speed → 45, disconnect → mapped offline; the temporary file is deleted and the API stopped afterward.
- [x] The Development CORS preflight against the running backend returns `Access-Control-Allow-Origin: http://localhost:5173` for the drone endpoints (verifying the browser path without backend changes).
- [x] No hardware/GPIO/pin/device references exist in any diff; the backend's drone side remains `MockDroneController` (no Raspberry Pi introduced); `dotnet build` 0 warnings/0 errors and all 51 backend tests still pass.
