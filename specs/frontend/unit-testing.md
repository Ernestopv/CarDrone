# Frontend Unit Testing

## Purpose

Establish an automated unit and component test suite for the Phase 1
frontend, so the behaviour defined in the existing specifications is verified
mechanically instead of by manual inspection.

This task is the executable side of the frontend final review: `npm test`
must prove the whole app's logic and UI states work together.

---

## Dependencies

```text
specs/frontend/drone-simulator.md
specs/frontend/camera.md
specs/frontend/keyboard-controls.md
specs/frontend/error-states.md
```

---

## Scope

Implement:

- a test runner wired to `npm test` in the frontend package;
- unit tests for pure logic (`utils/status`, keyboard mapping);
- unit tests for the mock service and all five mock scenarios;
- hook tests for `useDroneDashboard`;
- component tests for every feature component;
- an integration-style test of the assembled `App`;
- a documented mapping from each existing spec's acceptance criteria to tests.

---

## Out of Scope

Do not implement:

- end-to-end browser testing (Playwright, Cypress);
- visual regression, screenshots or snapshot-based tests;
- coverage reporting or coverage thresholds;
- performance, load or mutation testing;
- backend, network, GPIO, PWM, camera or uStreamer tests;
- production changes whose only purpose is testability (no test-only code
  paths, no test-only props);
- test parallelisation infrastructure or CI pipelines.

---

## Test Stack

Approved dependencies (all `devDependencies`, no production dependency):

```text
vitest                  test runner, reuses the Vite pipeline
@testing-library/react  render components and query them by role/text
@testing-library/dom    DOM query layer required by the React adapter
jsdom                   browser-like DOM environment
```

Rationale: the project's own stack (React, Vite, TypeScript) contains no test
runner, so one must be introduced. Vitest is the natural choice for a Vite
project because it uses the same configuration and transforms.

---

## Configuration

- `vitest.config.ts` in the frontend root, `environment: 'jsdom'`, matching
  `src/**/*.test.{ts,tsx}`.
- Scripts:

```text
npm test         → vitest run   (single pass, non-interactive)
npm run test:watch → vitest     (watch mode during development)
```

- Tests are colocated next to the code they test.
- Test files import `describe`/`it`/`expect` explicitly from `vitest`;
  no test globals.
- A setup file registers React Testing Library cleanup after every test.
- Test files live under `src`, so `npm run build` type-checks them and
  `npm run lint` lints them like any other source file.
- `vitest.config.ts` is included in `tsconfig.node.json` so the build
  type-checks it too.

---

## Test Design Rules

- Deterministic: no random values, no timing races, no test order
  dependence. Tests arrange their own state; the mock service singleton must
  be reset by the test that needs a specific starting point.
- No network access, no OS-specific APIs, no hardware.
- The mock's simulated delays (connect 700 ms, command ack 250 ms) may be
  awaited directly; tests must not add extra waiting beyond them.
- Query rendered output by role, label or visible text — never by CSS class.
- Simulated acknowledgements must always be asserted as simulated:
  `source === 'simulated'` and `confirmedByHardware === false`.
- Every test asserts observable behaviour (state, text, availability of
  controls), not internal implementation details.

---

## Required Test Suites

### 1. Status utilities — `src/utils/status.test.ts`

- initial status: offline drone, offline camera, commands at `stop`,
  `failedCommand` null, speed 0;
- `normalizeDroneStatus`: fills every missing field (including
  `failedCommand` → null) and passes a complete status through unchanged;
- `clampSpeed`: clamps below 0 and above 100, keeps in-range values;
- label helpers: command/connection/service labels and tones for every
  input value;
- state factories: connecting, error (drone + camera together),
  api-unavailable (API only), camera-failure (camera only), reset
  (error → connected, offline stays offline, commands stopped,
  `failedCommand` cleared, speed preserved).

### 2. Mock service — `src/mocks/mockDroneService.test.ts`

A fresh instance from `createMockDroneService()` per test.

- initial `getStatus` matches the initial status;
- `connect`: reaches connected, camera streaming, services online,
  commands reset, `failedCommand` cleared;
- `disconnect`: returns to the full offline status;
- `sendCommand` while offline: resolves without recording or confirming
  anything;
- `sendCommand` while connected: records the request, then confirms it after
  the simulated delay; the ack is always simulated;
- `setSpeed`: clamps values, ignored while offline.

### 3. Scenario service — `src/services/scenarioService.test.ts`

Covers error-states specification scenarios 1–5 against the real
`droneService`/`scenarioService` singletons:

- `connectionLost`: connection, camera and drone/camera services all error;
  movement commands are not confirmed while lost; reconnecting clears it;
- `apiUnavailable`: only the API service errors; commands keep working;
  `reset` restores it;
- `cameraFailure`: camera errors while the drone stays connected; commands
  keep working; `reset` restores streaming;
- `commandFailure`: `STOP` succeeds and does not consume the arm; the next
  movement command is rejected; `failedCommand` is recorded while
  `confirmedCommand` and the connection stay untouched; the following
  movement succeeds and clears the failure;
- `reset`: commands stopped, `failedCommand` null, speed preserved; an
  offline drone stays offline;
- compile-time assertion that the `DroneService` contract does not expose
  scenario triggers.

### 4. Keyboard mapping — `src/features/drone-control/useDroneKeyboardControls.test.ts`

- `commandForKeyboardEvent`: W/A/S/D and arrow keys map to the four
  movement commands; upper case works; Space maps to stop; key auto-repeat,
  Ctrl/Meta/Alt combinations, unmapped keys and editable targets
  (input/textarea/select/contentEditable) produce no command; an
  already-active command is ignored while stop is exempt;
- `handleDroneKeyDown`: dispatches the command; prevents default only for
  Space/arrow keys it actually handles;
- `useDroneKeyboardControls`: one window listener drives `onCommand`, and
  unmounting removes it.

### 5. Dashboard hook — `src/hooks/useDroneDashboard.test.ts`

Using `renderHook` from React Testing Library:

- initial state: offline, not pending, normalized status;
- `toggleConnection`: pending during the attempt, connected afterwards,
  back to offline on the next toggle;
- `sendCommand` while connected: request is optimistic (moves before the
  ack), confirmation follows only after the simulated ack;
- `sendCommand` while offline: movement ignored, `STOP` never crashes;
- `sendCommand` after `commandFailure`: `failedCommand` recorded,
  `confirmedCommand` unchanged, connection untouched;
- `changeSpeed`: updates status speed with clamping;
- `runScenario`: publishes the scenario status; `reset` restores it.

### 6. Components — one `*.test.tsx` per component

| Component | Required assertions |
| --- | --- |
| `CameraView` | all four camera states show their specified text; an unknown state falls back to a failure state, never a stream; OSD shows confirmed command and speed; state text has `role="status"` |
| `DroneControls` | movement buttons disabled while offline and enabled while connected; `STOP` always enabled; clicking a button calls `onCommand` with its command; active command is exposed via `aria-pressed`; speed slider disabled while offline and reports changes; keyboard hint present |
| `SystemStatusPanel` | all four service rows render with their labels and current values, including `Error` |
| `SystemAlerts` | renders nothing when healthy; each of the four alerts appears for its state; camera alert suppressed while the connection is lost; alerts use `role="alert"` and carry readable text |
| `TelemetryPanel` | pending marker while an ack is outstanding; `FAILED` marker for a failed command and no pending marker in that case |
| `MockScenariosPanel` | five triggers with `SIMULATED` marking; clicking fires the callback with the right scenario; all triggers disabled while `disabled`; `STOP never fails` hint present |
| `ConnectionStatus` | status label per connection state; `CONNECT`/`DISCONNECT` button; disabled while pending; click calls `onToggle` |

### 7. App integration — `src/App.test.tsx`

- renders the full dashboard (camera, controls, system status, telemetry,
  mock scenarios) with no alert while healthy;
- clicking `CONNECT` connects the app (controls become available);
- running the `CONNECTION LOST` scenario surfaces the alert and disables
  movement while `STOP` stays usable;
- after `RESET`, the alert clears again.

---

## Specification Coverage

Every acceptance criterion of the four existing specifications must be
covered by at least one test:

```text
drone-simulator → suites 1, 2, 5, 6 (DroneControls, TelemetryPanel)
camera          → suite 6 (CameraView)
keyboard-controls → suite 4
error-states    → suites 3, 6 (SystemAlerts, MockScenariosPanel), 7
```

The final report names any criterion that cannot be tested automatically.

---

## Platform Requirements

Standard APIs provided by jsdom only. The suite must run on:

```text
Windows
Linux
```

No operating-system-specific APIs, paths or shell commands; no network
access.

---

## Testing Scenarios

### Scenario 1 — Happy path

Connect the app, press `FORWARD`, wait for the simulated ack.

Expected: requested and confirmed command both end at `forward`, no alert.

### Scenario 2 — Command failure

Arm `commandFailure`, request `FORWARD`.

Expected: telemetry shows `FAILED`, a `COMMAND FAILED` alert appears, the
confirmed command does not move, `STOP` still works.

### Scenario 3 — Connection loss

Run `connectionLost`.

Expected: `CONNECTION LOST` alert, movement disabled, `STOP` available;
reconnecting or `RESET` clears the alert.

### Scenario 4 — Keyboard

Press `W`, `Space`, and a key inside a text input.

Expected: commands dispatched for the first two (Space prevents scrolling),
nothing for the input.

---

## Acceptance Criteria

- [x] `npm test` runs the full suite and exits 0.
- [x] `npm run test:watch` exists for interactive use.
- [x] `npm run build` passes with the test files type-checked.
- [x] `npm run lint` passes.
- [x] Every required suite exists and covers its listed assertions.
- [x] Each existing specification's acceptance criteria are covered by at
      least one test, with gaps reported.
- [x] New packages are devDependencies only.
- [x] No network, OS-specific or hardware access from tests.
- [x] Tests are deterministic and order-independent.
- [x] Simulated acknowledgements are always asserted as simulated.
- [x] The `DroneService` contract is proven free of scenario triggers at
      compile time.
- [x] No production code gained test-only paths.
