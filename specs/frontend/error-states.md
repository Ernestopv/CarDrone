# Frontend Error States

## Purpose

Represent the four failure situations that the dashboard must be able to show
during Phase 1, using mock scenarios only.

```text
Connection lost
Backend API unavailable
Camera unavailable
Command failure
```

This prepares the frontend for real failure handling later, without
introducing a backend, network calls, or hardware behaviour.

---

## Dependencies

```text
specs/frontend/drone-simulator.md
specs/frontend/camera.md
```

Reuse the existing camera error state, connection error state, service
statuses and `DroneService` flow.

---

## Scope

Implement:

- a visible UI state for each of the four failures;
- mock scenarios that make each failure reachable deterministically;
- recovery from each failure;
- a clear distinction between a failed command and an unconfirmed command.

---

## Out of Scope

Do not implement:

- a real backend or API;
- HTTP requests, SignalR, WebSockets, retries, timeouts;
- real Raspberry Pi, GPIO, PWM or motor behaviour;
- uStreamer or real camera access;
- toast/notification libraries;
- new state-management libraries;
- persistence of alerts;
- random or automatic failures;
- operating-system notifications.

---

## Architecture

Scenarios are a Phase 1 testing tool. They must stay out of `DroneService`,
because a future API implementation has no "simulate failure" endpoint.

```text
Mock Scenarios panel (UI)
        ↓
ScenarioService            ← Phase 1 only, mock only
        ↓
MockDroneService           ← same instance, same state as commands
        ↓
DroneStatus
        ↓
SystemAlerts / Telemetry / CameraView / SystemStatus / Header
```

Rules:

- `ScenarioService` is a separate interface; `DroneService` keeps its contract.
- Scenarios and commands must share one state (the same mock instance).
- The dashboard hook is the only caller of `ScenarioService`.
- Presentation components never apply scenarios and never mutate state.
- Command errors still flow through the existing `DroneService` path.

---

## State Model

Extend the existing `DroneStatus` with one field:

```ts
failedCommand: DroneCommand | null
```

- `null` means no command has failed.
- It is set when the service rejects a requested command.
- It is cleared when a new command is requested, when the drone connects,
  and when a scenario is reset.
- Normalization must default a missing value to `null`.

`requestedCommand` keeps its meaning (optimistic UI intent) and
`confirmedCommand` keeps its meaning (service acknowledgement only).
A failed command is never a confirmed command.

---

## Scenarios

All scenarios are explicit, deterministic and mock-only.

| Scenario | Effect | Recovery |
| --- | --- | --- |
| `connectionLost` | `connection = error`, `camera = error`, drone and camera services = error | Reconnect (Connect button) or Reset |
| `apiUnavailable` | `services.api = error`, everything else unchanged | Reset |
| `cameraFailure` | `camera = error`, `services.camera = error`, connection stays connected | Reset |
| `commandFailure` | Arms the mock to reject the **next movement command** | One-shot: clears itself after rejecting |
| `reset` | Clears scenario damage; returns to a state consistent with the connection | — |

Scenario rules:

- `STOP` must never fail, not even while `commandFailure` is armed.
- `commandFailure` is consumed only by a movement command; `STOP` passes
  through without consuming it.
- A rejected command records `failedCommand`, leaves `confirmedCommand`
  untouched, logs the error, and does not change the connection.
- `reset` must not fabricate a connection: an offline drone stays offline,
  an errored drone returns to `connected`, a connected drone keeps its
  connection. Commands return to `stop`, `failedCommand` returns to `null`,
  and speed is preserved.

---

## UI Requirements

### System Alerts

Create a component that lists active alerts derived from `DroneStatus`.

Alert triggers:

```text
connection = error                     → CONNECTION LOST
services.api = error                   → BACKEND API UNAVAILABLE
camera = error                         → CAMERA UNAVAILABLE
failedCommand != null                  → COMMAND FAILED
```

Rules:

- Presentation only; alerts are derived from props, never stored separately.
- Every alert carries readable text; colour is never the only signal.
- While the connection is lost, the camera alert is redundant and must be
  suppressed (the connection alert already explains it).
- Alerts disappear when the underlying state clears.
- No dismissal control in Phase 1: an alert exists only while the failure
  exists.
- The component renders nothing when there is no failure.

### Mock Scenarios Panel

A clearly marked panel providing one trigger per scenario plus `reset`:

```text
Connection lost
API unavailable
Camera failure
Command failure
Reset
```

Rules:

- The panel is visibly labelled as simulated/Phase 1 test triggers, so a
  simulated failure is never mistaken for a real one.
- Buttons call the dashboard hook's scenario handler; they never touch state
  directly.
- Buttons are disabled while a connection attempt is pending.
- A short hint explains that `Command failure` rejects the next movement
  command and that `STOP` never fails.

### Telemetry

- When `failedCommand` equals the requested command, the Requested Command
  row shows a `FAILED` marker.
- The pending marker on the Confirmed Command row (`…`) must not be shown
  while that command has failed; it appears only while an acknowledgement is
  genuinely outstanding.

### Existing Surfaces

The following already show failure states and must keep working unchanged:

- `Header` connection indicator (`ERROR`).
- `SystemStatusPanel` rows (`Error` tone).
- `CameraView` (`STREAM ERROR`).

---

## Safety UI

- A failed command must never be rendered as confirmed.
- Acknowledgements stay simulated: `CommandAck.source = "simulated"`,
  `confirmedByHardware = false`.
- Simulated scenario triggers stay visibly marked as mock.
- Connection loss must disable movement controls through the existing
  `connected` check; `STOP` stays available.

---

## Accessibility

- Each alert uses `role="alert"`.
- Alert titles are real text, not icon-only.
- Scenario triggers are real buttons with visible labels and visible focus.
- No state is conveyed by colour alone.

---

## Platform Requirements

Standard browser APIs only. The implementation must work on:

```text
Windows
Linux
```

No operating-system-specific APIs, paths or shell commands.

---

## Testing Scenarios

### Scenario 1 — Connection Lost

Given the drone is connected.

Run `connectionLost`.

Expected:

```text
CONNECTION LOST alert visible
header connection = ERROR
camera shows a failure state
movement controls disabled, STOP available
```

Reconnect: alerts clear, camera streams again.

---

### Scenario 2 — API Unavailable

Run `apiUnavailable`.

Expected:

```text
BACKEND API UNAVAILABLE alert visible
System Status API row = Error
drone connection unaffected; commands still work
```

Reset: alert clears, API row returns to its normal value.

---

### Scenario 3 — Camera Unavailable

Given the drone is connected.

Run `cameraFailure`.

Expected:

```text
CAMERA UNAVAILABLE alert visible
CameraView shows STREAM ERROR
commands still work
```

Reset: alert clears, camera returns to streaming.

---

### Scenario 4 — Command Failure

Given the drone is connected and `commandFailure` is armed.

Request `forward`.

Expected:

```text
requestedCommand = forward
confirmedCommand unchanged (previous value)
COMMAND FAILED alert visible
telemetry shows FAILED on the requested row
error is logged
connection stays connected
```

Press `Space` (STOP): still succeeds — STOP never fails.

Request another movement command: it succeeds; the alert clears.

---

### Scenario 5 — Reset

After any scenario, run `reset`.

Expected:

```text
alerts clear
commands = stop / stop
failedCommand = null
speed preserved
offline stays offline; error returns to connected
```

---

## Acceptance Criteria

- [x] Connection lost state is represented in the UI.
- [x] API unavailable state is represented in the UI.
- [x] Camera unavailable state is represented in the UI.
- [x] Command failure state is represented in the UI.
- [x] Every state is reachable through mock scenarios only.
- [x] No real network request produces any error state.
- [x] Scenario control lives outside `DroneService`.
- [x] Scenarios share the existing mock service state; no second state store.
- [x] Presentation components do not apply scenarios or mutate state.
- [x] A failed command is never reported as confirmed.
- [x] `failedCommand` never claims hardware confirmation.
- [x] STOP never fails in the command-failure scenario.
- [x] Alerts are readable text, not colour alone.
- [x] Alerts use `role="alert"`.
- [x] Scenario triggers are visibly marked as simulated.
- [x] Movement controls are disabled after connection loss; STOP stays usable.
- [x] Recovery works: reconnect and reset clear their alerts.
- [x] Existing camera, connection, status and telemetry behaviour still works.
- [x] Build passes.
- [x] Lint passes.
- [x] No backend, GPIO, PWM, uStreamer or platform-specific code is introduced.
