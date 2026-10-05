# Product Requirements Document

## Product Name

Drone Control

## Version

MVP — Phase 1 Frontend

## Status

Initial definition

---

# 1. Product Overview

Drone Control is a web application designed to monitor and control a physical drone.

The final system will include:

- A React frontend.
- A .NET backend.
- A Raspberry Pi installed on the drone.
- GPIO-based hardware control.
- PWM outputs.
- A Raspberry Pi camera.
- uStreamer for video streaming.

Development will be incremental.

The first phase focuses exclusively on the frontend.

No physical drone control will be implemented during Phase 1.

---

# 2. Product Goal

The goal of the application is to provide a centralized control dashboard where a user can:

- View the drone camera.
- See drone connection status.
- Send movement commands.
- Stop the drone immediately.
- View telemetry.
- Monitor Raspberry Pi status.
- Monitor camera status.
- Monitor backend connectivity.
- Control drone speed when supported.

The interface should be designed so that mock services used during frontend development can later be replaced by real backend services.

---

# 3. Current Scope

## Phase 1 — Frontend MVP

The current scope includes only:

- React frontend.
- TypeScript.
- Dashboard interface.
- Simulated telemetry.
- Simulated connection states.
- Simulated drone commands.
- Camera placeholder.
- Responsive interface.

The following are explicitly excluded from Phase 1:

- .NET backend.
- Raspberry Pi communication.
- GPIO communication.
- PWM.
- Physical motor control.
- Real camera stream.
- uStreamer configuration.
- WebSockets.
- SignalR.
- Authentication.
- Database.

---

# 4. Target User

The initial application is intended for a technical operator controlling a drone from a desktop or laptop.

The operator should be able to understand the drone's state without navigating through multiple screens.

The primary interface should therefore behave like a control dashboard rather than a traditional website.

---

# 5. Main Application Screen

The MVP will initially contain a single main dashboard.

Suggested layout:

```text
┌─────────────────────────────────────────────────────────────┐
│ DRONE CONTROL                              ● DISCONNECTED   │
├────────────────────────────────────────────┬────────────────┤
│                                            │ SYSTEM STATUS  │
│                                            │                │
│                                            │ Drone      ●   │
│               CAMERA VIEW                  │ Raspberry  ●   │
│                                            │ Camera     ●   │
│                                            │ API        ●   │
│                                            │                │
├────────────────────────────────────────────┼────────────────┤
│                                            │ TELEMETRY      │
│                  ▲                         │                │
│               ◀  ■  ▶                      │ Command: STOP  │
│                  ▼                         │ Speed: 0%      │
│                                            │ Signal: --     │
└────────────────────────────────────────────┴────────────────┘
```

The exact visual design may evolve, but the functional areas should remain clearly separated.

---

# 6. Header

The application header should display:

- Product name.
- Global connection status.
- Optional system status indicator.

Example:

```text
DRONE CONTROL

● CONNECTED
```

Possible connection states:

```text
DISCONNECTED
CONNECTING
CONNECTED
ERROR
```

The status must be visually distinguishable.

---

# 7. Camera Area

The camera panel is one of the primary elements of the interface.

During Phase 1, it will contain a placeholder.

The component should eventually support a real video stream without requiring a complete redesign.

## Camera states

The camera component must support:

```text
offline
connecting
streaming
error
```

Example placeholder:

```text
┌───────────────────────────────────────┐
│                                       │
│                                       │
│              CAMERA                   │
│                                       │
│             NO SIGNAL                 │
│                                       │
│                                       │
└───────────────────────────────────────┘
```

During the frontend phase, these states will be simulated.

---

# 8. Drone Control Panel

The dashboard must provide directional controls.

Initial commands:

```text
FORWARD
BACKWARD
LEFT
RIGHT
STOP
```

Suggested visual arrangement:

```text
        ▲
        │
     FORWARD

LEFT ◀   ■   ▶ RIGHT

     BACKWARD
        │
        ▼
```

The central control represents:

```text
STOP
```

During Phase 1, clicking a control should update local application state only.

No network request should be required.

---

# 9. Command Model

Commands should be represented semantically.

Example:

```text
FORWARD
BACKWARD
LEFT
RIGHT
STOP
```

The frontend must never know which Raspberry Pi GPIO pins correspond to these commands.

For example:

```text
React
   ↓
FORWARD
   ↓
.NET Backend
   ↓
Hardware layer
   ↓
GPIO
```

The mapping between commands and GPIO will be handled later by the backend/hardware layer.

---

# 10. Speed Control

The interface should reserve space for future speed control.

Initial representation:

```text
Speed

0 ─────────●───────── 100%
```

During Phase 1 this value is simulated.

Possible values:

```text
0 - 100
```

The frontend should store it as a percentage.

Example:

```text
50%
```

The relationship between speed percentage and PWM output will be defined later.

---

# 11. System Status

The interface must provide a dedicated status area.

Services/devices to display:

```text
Drone
Raspberry Pi
Camera
Backend API
```

Each can have states such as:

```text
offline
connecting
online
error
```

Example:

```text
SYSTEM STATUS

Drone          ● Offline
Raspberry Pi   ● Offline
Camera         ● Offline
API            ● Offline
```

During Phase 1 these values come from mock data.

---

# 12. Telemetry

The telemetry panel should display information about the current simulated state.

Initial telemetry:

```text
Current command
Speed
Connection state
Camera state
```

Example:

```text
TELEMETRY

Command       FORWARD
Speed         50%
Connection    CONNECTED
Camera        STREAMING
```

Additional telemetry may be introduced later when real hardware sensors are available.

Do not invent sensor measurements during the initial frontend phase.

---

# 13. Mock Drone

The frontend should include a simulated drone service.

The mock service should allow the user interface to behave as though a drone exists.

Example state:

```ts
{
  connection: "connected",
  camera: "streaming",
  raspberryPi: "connected",
  command: "stop",
  speed: 0
}
```

Commands executed through the interface should modify this simulated state.

---

# 14. Initial User Flow

When the application opens:

1. The dashboard loads.
2. The drone initially appears disconnected.
3. The camera shows no signal.
4. Controls remain visible.
5. The application may allow switching to a simulated connected state.

For development purposes, mock state may simulate:

```text
Connecting...
Connected
```

Once connected:

1. System indicators become active.
2. Camera may change to simulated streaming state.
3. Directional controls become available.
4. Telemetry updates based on interactions.

---

# 15. Command Interaction

Example interaction:

User presses:

```text
FORWARD
```

Frontend updates:

```text
Current Command: FORWARD
```

User presses:

```text
LEFT
```

Frontend updates:

```text
Current Command: LEFT
```

User presses:

```text
STOP
```

Frontend updates:

```text
Current Command: STOP
```

No real hardware communication occurs.

---

# 16. Keyboard Controls

The frontend architecture should allow keyboard control.

Initial proposal:

```text
W        Forward
S        Backward
A        Left
D        Right
Space    Stop
```

Keyboard controls should not be implemented until the basic visual controls are stable.

They should be treated as a later frontend task.

---

# 17. Responsive Design

Primary target:

```text
Desktop / Laptop
```

The dashboard should work correctly from approximately:

```text
1280px width and above
```

Secondary support:

```text
Tablet
```

Mobile support is not a priority for the initial MVP because drone operation requires sufficient screen space for video and controls.

The application must not become unusable on smaller screens, but mobile-specific optimization can be deferred.

---

# 18. Visual Style

The application should resemble a modern technical control panel.

Desired characteristics:

- Dark theme.
- Minimal interface.
- High contrast.
- Large camera area.
- Clear controls.
- Clear connection indicators.
- Technical but readable appearance.
- Low visual noise.

Avoid excessive gradients, animations, decorations, and unnecessary effects.

The interface should prioritize usability over visual complexity.

---

# 19. Component Requirements

Suggested major components:

```text
AppShell
Header
ConnectionStatus
CameraView
DroneControls
DirectionButton
SpeedControl
SystemStatusPanel
StatusIndicator
TelemetryPanel
```

Components should remain independent where practical.

---

# 20. Frontend Domain Model

Initial domain concepts:

```text
DroneStatus
DroneCommand
ConnectionStatus
CameraStatus
SystemStatus
Telemetry
```

Example command type:

```ts
type DroneCommand =
  | "forward"
  | "backward"
  | "left"
  | "right"
  | "stop";
```

Example connection status:

```ts
type ConnectionStatus =
  | "offline"
  | "connecting"
  | "connected"
  | "error";
```

---

# 21. Future Hardware Configuration

The final system will use a Raspberry Pi.

Current planned GPIO configuration: the single-source record lives in
[`docs/hardware/WIRING.md`](hardware/WIRING.md) (section "Given
configuration"); values match `backend/src/DroneControl.Api/appsettings.json`.

These pins are documented for future integration.

They must NOT be used by frontend code.

Their exact hardware semantics will be defined during backend development.

---

# 22. Future Video Architecture

The planned video architecture is:

```text
Raspberry Pi Camera
        ↓
     uStreamer
        ↓
      MJPEG
        ↓
 React CameraView
```

The exact endpoint and network configuration will be defined later.

During Phase 1:

```text
CameraView
    ↓
Placeholder / Mock
```

---

# 23. Future Backend Architecture

Expected high-level architecture:

```text
React
  ↓
.NET API
  ↓
Application Services
  ↓
Drone Hardware Service
  ↓
Raspberry Pi
  ↓
GPIO
```

Real-time communication may later use:

```text
SignalR
```

This decision is not required for Phase 1.

---

# 24. Safety Requirements

The final physical system must treat movement commands as potentially safety-critical.

The UI architecture should therefore anticipate:

- Connection loss.
- Command failure.
- Hardware unavailable.
- Backend unavailable.
- Camera failure.

The frontend must distinguish between:

```text
Command requested
```

and:

```text
Command confirmed by hardware
```

During Phase 1, command confirmation is simulated only.

---

# 25. MVP Acceptance Criteria

The frontend MVP is complete when:

- [x] The application starts correctly.
- [x] The dashboard loads.
- [x] The camera placeholder is visible.
- [x] Direction controls are visible.
- [x] STOP is available.
- [x] Speed can be represented.
- [x] System status is displayed.
- [x] Telemetry is displayed.
- [x] Mock connection states work.
- [x] Mock commands update telemetry.
- [x] The application is responsive on desktop.
- [x] TypeScript builds successfully.
- [x] No .NET code has been introduced.
- [x] No GPIO communication exists.
- [x] No physical hardware is required to run the frontend.

---

# 26. Development Order

Implementation should follow this order:

## Task 1

Initialize:

```text
React
TypeScript
Vite
```

## Task 2

Create base application layout.

## Task 3

Create dashboard structure.

## Task 4

Create `CameraView`.

## Task 5

Create system status panel.

## Task 6

Create telemetry panel.

## Task 7

Create drone directional controls.

## Task 8

Create STOP control.

## Task 9

Create speed control.

## Task 10

Create mock drone service.

## Task 11

Connect components to simulated state.

## Task 12

Improve responsive design and UI polish.

---

# 27. Out of Scope

Do not implement during this phase:

```text
ASP.NET Core
SignalR
Raspberry Pi
GPIO
PWM
uStreamer
Real camera
Real motors
Authentication
Users
Database
Cloud deployment
Mobile application
```

These features will be introduced progressively after the frontend MVP is stable.