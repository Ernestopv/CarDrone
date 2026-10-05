# Camera Simulation

## Purpose

Improve the existing `CameraView` component so the frontend can represent the complete lifecycle of the drone camera using simulated state.

This specification prepares the frontend for future integration with a real uStreamer video source without implementing real streaming yet.

---

## Scope

Implement support for these camera states:

```text
offline
connecting
streaming
error
```

Improve `CameraView` so each state is represented clearly in the UI.

The camera state must continue to come from the existing drone simulator layer.

---

## Dependencies

This specification depends on the existing simulator specification:

```text
specs/frontend/drone-simulator.md
```

Reuse the existing camera state exposed by:

```text
DroneService
↓
MockDroneService
↓
React UI
```

Do not create a second independent camera state system.

---

## Out of Scope

Do not implement:

- uStreamer;
- MJPEG;
- WebRTC;
- real camera access;
- browser camera APIs;
- Raspberry Pi camera access;
- backend endpoints;
- HTTP video streaming;
- SignalR;
- WebSockets;
- GPIO;
- PWM;
- platform-specific camera code.

---

## Platform Requirements

The frontend implementation must remain platform-independent.

It must work in a modern browser when the frontend is run on:

```text
Windows
Linux
```

Do not use:

- Windows-specific paths;
- Linux-specific paths;
- shell commands from React;
- operating-system-specific APIs;
- native camera drivers.

The camera simulation must run entirely in React/TypeScript.

---

## Camera State Model

Reuse or define:

```ts
export type CameraStatus = "offline" | "connecting" | "streaming" | "error";
```

Do not duplicate this type if it already exists.

---

## CameraView Contract

`CameraView` should receive explicit props.

Suggested contract:

```ts
interface CameraViewProps {
  status: CameraStatus;
  streamUrl?: string;
}
```

`streamUrl` is optional and reserved for future integration.

Do not require a real stream URL during this task.

If the existing architecture has a cleaner equivalent, keep the existing conventions.

---

## State: Offline

When camera status is:

```text
offline
```

display a clear offline state.

Example:

```text
CAMERA

NO SIGNAL
```

The component must not attempt to load video.

---

## State: Connecting

When camera status is:

```text
connecting
```

display a connecting state.

Example:

```text
CAMERA

CONNECTING...
```

A lightweight loading indicator may be used.

Avoid unnecessary animation libraries.

---

## State: Streaming

When camera status is:

```text
streaming
```

display a simulated active stream state.

Example:

```text
CAMERA ACTIVE

SIMULATED STREAM
```

The purpose of this state is to verify the UI behavior before real video integration.

Do not fetch or render a real video source.

---

## State: Error

When camera status is:

```text
error
```

display a clear error state.

Example:

```text
CAMERA

STREAM ERROR
```

The state should be visually distinguishable from `offline`.

---

## Simulator Integration

The camera status must be driven by the existing simulator.

Expected behavior:

```text
Drone disconnected
↓
Camera offline
```

```text
Drone connecting
↓
Camera connecting
```

```text
Drone connected
↓
Camera streaming
```

If the simulator exposes an error state:

```text
Drone/Camera error
↓
Camera error
```

Do not duplicate simulator timing or connection logic inside `CameraView`.

`CameraView` is responsible for presentation only.

---

## Future Streaming Boundary

Prepare `CameraView` so a real stream can later be introduced without changing the dashboard architecture.

Future conceptual flow:

```text
Mock camera
    ↓
CameraView
```

will become:

```text
uStreamer
    ↓
streamUrl
    ↓
CameraView
```

The current implementation must not depend on uStreamer-specific URLs or formats.

---

## Presentation Requirements

The camera area should:

- preserve its current dashboard size and hierarchy;
- maintain a stable aspect ratio;
- clearly show the current camera state;
- avoid layout shifts between states;
- remain readable on desktop and tablet layouts;
- keep the visual style consistent with the existing dashboard.

---

## Accessibility

Camera state must not be communicated by color alone.

Each state must include readable text.

Examples:

```text
NO SIGNAL
CONNECTING...
SIMULATED STREAM
STREAM ERROR
```

If a loading indicator is used, it should not be the only indication of state.

---

## Error Handling

`CameraView` must handle unexpected or missing state safely.

TypeScript should prevent unsupported states where possible.

Do not silently treat an error as a successful stream.

---

## Testing and Validation

Validate at least these scenarios:

### Scenario 1 — Offline

```text
camera = offline
```

Expected:

```text
NO SIGNAL
```

### Scenario 2 — Connecting

```text
camera = connecting
```

Expected:

```text
CONNECTING...
```

### Scenario 3 — Streaming

```text
camera = streaming
```

Expected:

```text
SIMULATED STREAM
```

### Scenario 4 — Error

```text
camera = error
```

Expected:

```text
STREAM ERROR
```

### Scenario 5 — Simulator Integration

Connect the simulated drone.

Expected:

```text
offline
↓
connecting
↓
streaming
```

Disconnect the simulated drone.

Expected:

```text
streaming
↓
offline
```

---

## Acceptance Criteria

- [x] `CameraView` supports `offline`.
- [x] `CameraView` supports `connecting`.
- [x] `CameraView` supports `streaming`.
- [x] `CameraView` supports `error`.
- [x] Camera state comes from the existing drone simulator.
- [x] No duplicate camera state is introduced in presentation components.
- [x] `CameraView` does not contain simulator connection logic.
- [x] Streaming state remains simulated.
- [x] No real network video request is made.
- [x] No uStreamer integration exists.
- [x] No platform-specific Windows code exists.
- [x] No platform-specific Linux code exists.
- [x] The frontend remains usable on Windows and Linux.
- [x] Camera state is represented with text, not only color.
- [x] Layout remains stable between camera states.
- [x] Build passes.
- [x] Lint passes.
